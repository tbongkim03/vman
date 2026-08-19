// vman 탐색기 셸 확장 — 윈도우 11 상단 컨텍스트 메뉴용 IExplorerCommand 핸들러.
//
// 왜 C++ 인가.
//   윈도우 11 의 새 컨텍스트 메뉴는 고전 레지스트리 항목을 받아 주지 않는다.
//   MSIX 로 등록한 COM 핸들러만 상단에 올라간다. 그리고 이 DLL 은 탐색기와 같은
//   프로세스 공간에서 도는 대리자(surrogate)에 로드되므로, CLR 을 끌어들이면 안 된다.
//   그래서 런타임 의존성이 없는 C++ 로 쓴다.
//
// 하는 일은 단순하다. 메뉴 항목 세 개(루트 + 하위 둘)를 만들고, 클릭하면
// vman-tray.exe --venv "<폴더>" "<이름>" 을 띄운다. 실제 가상환경 생성은 전부
// 그쪽이 한다. 이 DLL 은 파이썬도 vman 도 알지 못한다.

#include <windows.h>
#include <shobjidl_core.h>
#include <shlwapi.h>
#include <shlobj_core.h>
#include <wrl/implements.h>
#include <wrl/module.h>
#include <cstdarg>
#include <cstdio>
#include <string>
#include <vector>

using namespace Microsoft::WRL;

// {FEE6FAFD-826C-4B04-B553-B3DB7616DB8B} — 매니페스트의 Clsid 와 반드시 같아야 한다.
class __declspec(uuid("FEE6FAFD-826C-4B04-B553-B3DB7616DB8B")) VManVenvCommand;

namespace {

// ---------- 진단 로그 ----------
//
// 셸 확장은 탐색기가 띄우는 대리자 안에서 돌아 디버거를 붙이기 어려우므로,
// 어디까지 실행됐는지 남길 방법이 하나는 있어야 한다.
//
// 스위치를 환경변수로 두지 않는 이유: 대리자는 DCOM 이 띄우므로 사용자 셸의
// 환경변수를 물려받지 못한다. 대신 마커 파일이 있을 때만 기록한다.
//     type nul > C:\Users\Public\vman-shellext.on
// 컨테이너 안에서도 접근 가능한 C:\Users\Public 을 쓴다.
void Log(const wchar_t* fmt, ...)
{
    if (GetFileAttributesW(L"C:\\Users\\Public\\vman-shellext.on") == INVALID_FILE_ATTRIBUTES)
        return;

    wchar_t line[2048];
    va_list args;
    va_start(args, fmt);
    int n = _vsnwprintf_s(line, _TRUNCATE, fmt, args);
    va_end(args);
    if (n <= 0) return;

    HANDLE h = CreateFileW(L"C:\\Users\\Public\\vman-shellext.log",
                           FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE,
                           nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (h == INVALID_HANDLE_VALUE) return;

    std::wstring text(line);
    text += L"\r\n";
    int bytes = WideCharToMultiByte(CP_UTF8, 0, text.c_str(), -1, nullptr, 0, nullptr, nullptr);
    if (bytes > 1)
    {
        std::vector<char> utf8(bytes);
        WideCharToMultiByte(CP_UTF8, 0, text.c_str(), -1, utf8.data(), bytes, nullptr, nullptr);
        DWORD written = 0;
        WriteFile(h, utf8.data(), static_cast<DWORD>(bytes - 1), &written, nullptr);
    }
    CloseHandle(h);
}

// ---------- 공통 헬퍼 ----------

/// 우클릭한 폴더의 경로를 얻는다.
/// 폴더 아이콘을 우클릭하면 그 폴더가 selection 으로 들어오고,
/// 폴더 안 빈 공간을 우클릭하면 selection 이 비어 있어 탐색기가 site 로 현재 폴더를 준다.
HRESULT GetTargetFolder(IShellItemArray* selection, IUnknown* site, std::wstring& out)
{
    out.clear();

    if (selection)
    {
        DWORD count = 0;
        if (SUCCEEDED(selection->GetCount(&count)) && count > 0)
        {
            ComPtr<IShellItem> item;
            if (SUCCEEDED(selection->GetItemAt(0, &item)))
            {
                PWSTR path = nullptr;
                if (SUCCEEDED(item->GetDisplayName(SIGDN_FILESYSPATH, &path)) && path)
                {
                    out.assign(path);
                    CoTaskMemFree(path);
                    return S_OK;
                }
            }
        }
    }

    // 빈 공간 우클릭: 탐색기가 붙여 준 site 를 타고 현재 보고 있는 폴더를 알아낸다.
    if (site)
    {
        ComPtr<IServiceProvider> services;
        if (SUCCEEDED(site->QueryInterface(IID_PPV_ARGS(&services))))
        {
            ComPtr<IFolderView> view;
            if (SUCCEEDED(services->QueryService(SID_SFolderView, IID_PPV_ARGS(&view))))
            {
                ComPtr<IShellItem> folder;
                if (SUCCEEDED(view->GetFolder(IID_PPV_ARGS(&folder))))
                {
                    PWSTR path = nullptr;
                    if (SUCCEEDED(folder->GetDisplayName(SIGDN_FILESYSPATH, &path)) && path)
                    {
                        out.assign(path);
                        CoTaskMemFree(path);
                        return S_OK;
                    }
                }
            }
        }
    }

    return E_FAIL;
}

/// %LOCALAPPDATA%\vman\bin\<name> 를 만든다.
/// VMAN_ROOT 를 존중해서, 루트를 옮겨 쓰는 사람도 메뉴가 동작하게 한다.
std::wstring VmanBinPath(const wchar_t* exeName)
{
    std::wstring root;

    wchar_t buffer[MAX_PATH * 2] = {};
    DWORD n = GetEnvironmentVariableW(L"VMAN_ROOT", buffer, ARRAYSIZE(buffer));
    if (n > 0 && n < ARRAYSIZE(buffer))
    {
        root.assign(buffer, n);
    }
    else
    {
        // KF_FLAG_NO_PACKAGE_REDIRECTION 이 없으면 안 된다.
        // 이 DLL 은 탐색기가 띄우는 패키지 COM 대리자 안에서 도는데, 그 안에서
        // FOLDERID_LocalAppData 는 패키지 전용 위치로 리다이렉트된다.
        //   ...\AppData\Local\Packages\VMan.ShellExt_<해시>\LocalCache\Local
        // 거기에는 vman-tray.exe 가 없으므로 GetState 가 ECS_HIDDEN 을 돌려주고,
        // 메뉴가 아예 나타나지 않는다.
        PWSTR local = nullptr;
        if (FAILED(SHGetKnownFolderPath(FOLDERID_LocalAppData,
                                        KF_FLAG_NO_PACKAGE_REDIRECTION, nullptr, &local)))
            return std::wstring();
        root.assign(local);
        CoTaskMemFree(local);
        root += L"\\vman";
    }

    root += L"\\bin\\";
    root += exeName;
    Log(L"VmanBinPath -> %s", root.c_str());
    return root;
}

/// 인자를 큰따옴표로 감싼다. 경로에 공백이 흔하므로 반드시 필요하다.
std::wstring Quote(const std::wstring& s)
{
    std::wstring out = L"\"";
    for (wchar_t c : s)
    {
        if (c == L'"') out += L'\\';
        out += c;
    }
    out += L'"';
    return out;
}

// ---------- 하위 항목 (.venv / venv) ----------

/// 하위 메뉴 항목 하나. 클릭하면 vman-tray.exe 를 띄운다.
class VenvSubCommand
    : public RuntimeClass<RuntimeClassFlags<ClassicCom>, IExplorerCommand, IObjectWithSite>
{
public:
    VenvSubCommand(std::wstring folderName, std::wstring title)
        : m_folderName(std::move(folderName)), m_title(std::move(title)) {}

    IFACEMETHODIMP GetTitle(IShellItemArray*, PWSTR* name) override
    {
        return SHStrDupW(m_title.c_str(), name);
    }
    IFACEMETHODIMP GetIcon(IShellItemArray*, PWSTR* icon) override
    {
        *icon = nullptr;
        return E_NOTIMPL;
    }
    IFACEMETHODIMP GetToolTip(IShellItemArray*, PWSTR* tip) override
    {
        *tip = nullptr;
        return E_NOTIMPL;
    }
    IFACEMETHODIMP GetCanonicalName(GUID* guid) override { *guid = GUID_NULL; return S_OK; }
    IFACEMETHODIMP GetState(IShellItemArray*, BOOL, EXPCMDSTATE* state) override
    {
        *state = ECS_ENABLED;
        return S_OK;
    }
    IFACEMETHODIMP GetFlags(EXPCMDFLAGS* flags) override { *flags = ECF_DEFAULT; return S_OK; }
    IFACEMETHODIMP EnumSubCommands(IEnumExplorerCommand** e) override { *e = nullptr; return E_NOTIMPL; }

    IFACEMETHODIMP Invoke(IShellItemArray* selection, IBindCtx*) override
    {
        Log(L"sub Invoke: name=%s selection=%p site=%p",
            m_folderName.c_str(), selection, m_site.Get());

        std::wstring folder;
        HRESULT hrFolder = GetTargetFolder(selection, m_site.Get(), folder);
        Log(L"sub Invoke: GetTargetFolder hr=%08X folder=[%s]", hrFolder, folder.c_str());
        if (FAILED(hrFolder) || folder.empty())
            return E_FAIL;

        // 창 없는 트레이 실행 파일에 맡긴다. 콘솔 앱을 띄우면 검은 창이 번쩍인다.
        std::wstring exe = VmanBinPath(L"vman-tray.exe");
        if (exe.empty() || GetFileAttributesW(exe.c_str()) == INVALID_FILE_ATTRIBUTES)
        {
            Log(L"sub Invoke: exe not found");
            return E_FAIL;
        }

        std::wstring args = L"--venv " + Quote(folder) + L" " + Quote(m_folderName);

        SHELLEXECUTEINFOW info = { sizeof(info) };
        info.fMask = SEE_MASK_NOASYNC;
        info.lpVerb = L"open";
        info.lpFile = exe.c_str();
        info.lpParameters = args.c_str();
        info.lpDirectory = folder.c_str();
        info.nShow = SW_SHOWNORMAL;

        BOOL ok = ShellExecuteExW(&info);
        Log(L"sub Invoke: ShellExecuteEx ok=%d err=%lu hInstApp=%p args=%s",
            ok, ok ? 0UL : GetLastError(), info.hInstApp, args.c_str());
        return ok ? S_OK : HRESULT_FROM_WIN32(GetLastError());
    }

    IFACEMETHODIMP SetSite(IUnknown* site) override { m_site = site; return S_OK; }
    IFACEMETHODIMP GetSite(REFIID riid, void** ppv) override { return m_site.CopyTo(riid, ppv); }

private:
    std::wstring m_folderName;
    std::wstring m_title;
    ComPtr<IUnknown> m_site;
};

/// 하위 항목들을 탐색기에 넘겨주는 열거자.
class VenvSubCommandEnum
    : public RuntimeClass<RuntimeClassFlags<ClassicCom>, IEnumExplorerCommand>
{
public:
    explicit VenvSubCommandEnum(std::vector<ComPtr<IExplorerCommand>> items)
        : m_items(std::move(items)) {}

    IFACEMETHODIMP Next(ULONG count, IExplorerCommand** out, ULONG* fetched) override
    {
        ULONG made = 0;
        for (ULONG i = 0; i < count && m_index < m_items.size(); ++i, ++m_index)
            m_items[m_index].CopyTo(&out[made++]);

        if (fetched) *fetched = made;
        return made == count ? S_OK : S_FALSE;
    }
    IFACEMETHODIMP Skip(ULONG count) override { m_index += count; return S_OK; }
    IFACEMETHODIMP Reset() override { m_index = 0; return S_OK; }
    IFACEMETHODIMP Clone(IEnumExplorerCommand**) override { return E_NOTIMPL; }

private:
    std::vector<ComPtr<IExplorerCommand>> m_items;
    size_t m_index = 0;
};

} // namespace

// ---------- 루트 항목 ----------

/// 우클릭 메뉴에 보이는 최상위 항목. 하위 둘을 달고 있다.
class VManVenvCommand
    : public RuntimeClass<RuntimeClassFlags<ClassicCom>, IExplorerCommand, IObjectWithSite>
{
public:
    IFACEMETHODIMP GetTitle(IShellItemArray*, PWSTR* name) override
    {
        Log(L"root GetTitle");
        return SHStrDupW(L"vman 가상환경 만들기", name);
    }
    IFACEMETHODIMP GetIcon(IShellItemArray*, PWSTR* icon) override
    {
        std::wstring exe = VmanBinPath(L"vman-tray.exe");
        if (exe.empty()) { *icon = nullptr; return E_NOTIMPL; }
        exe += L",0";
        return SHStrDupW(exe.c_str(), icon);
    }
    IFACEMETHODIMP GetToolTip(IShellItemArray*, PWSTR* tip) override { *tip = nullptr; return E_NOTIMPL; }
    IFACEMETHODIMP GetCanonicalName(GUID* guid) override { *guid = GUID_NULL; return S_OK; }

    IFACEMETHODIMP GetState(IShellItemArray*, BOOL, EXPCMDSTATE* state) override
    {
        // vman 이 설치되어 있지 않으면 메뉴를 아예 숨긴다.
        std::wstring exe = VmanBinPath(L"vman-tray.exe");
        DWORD attr = exe.empty() ? INVALID_FILE_ATTRIBUTES : GetFileAttributesW(exe.c_str());
        bool ok = attr != INVALID_FILE_ATTRIBUTES;
        Log(L"root GetState: exe=%s attr=%08X err=%lu -> %s",
            exe.c_str(), attr, ok ? 0UL : GetLastError(), ok ? L"ENABLED" : L"HIDDEN");
        *state = ok ? ECS_ENABLED : ECS_HIDDEN;
        return S_OK;
    }

    // 하위 메뉴를 가지려면 이 플래그가 있어야 한다.
    IFACEMETHODIMP GetFlags(EXPCMDFLAGS* flags) override
    {
        Log(L"root GetFlags");
        *flags = ECF_HASSUBCOMMANDS;
        return S_OK;
    }

    IFACEMETHODIMP EnumSubCommands(IEnumExplorerCommand** out) override
    {
        *out = nullptr;

        std::vector<ComPtr<IExplorerCommand>> items;
        items.push_back(Make<VenvSubCommand>(L".venv", L".venv  (숨김)"));
        items.push_back(Make<VenvSubCommand>(L"venv", L"venv"));

        // 하위 항목도 site 를 알아야 빈 공간 우클릭에서 현재 폴더를 찾을 수 있다.
        for (auto& item : items)
        {
            ComPtr<IObjectWithSite> withSite;
            if (SUCCEEDED(item.As(&withSite))) withSite->SetSite(m_site.Get());
        }

        auto e = Make<VenvSubCommandEnum>(std::move(items));
        return e->QueryInterface(IID_PPV_ARGS(out));
    }

    IFACEMETHODIMP Invoke(IShellItemArray*, IBindCtx*) override { return S_OK; }

    IFACEMETHODIMP SetSite(IUnknown* site) override { m_site = site; return S_OK; }
    IFACEMETHODIMP GetSite(REFIID riid, void** ppv) override { return m_site.CopyTo(riid, ppv); }

private:
    ComPtr<IUnknown> m_site;
};

CoCreatableClass(VManVenvCommand)

// ---------- DLL 진입점 ----------

STDAPI DllGetClassObject(REFCLSID rclsid, REFIID riid, void** ppv)
{
    HRESULT hr = Module<InProc>::GetModule().GetClassObject(rclsid, riid, ppv);
    Log(L"DllGetClassObject hr=%08X", hr);
    return hr;
}

STDAPI DllCanUnloadNow()
{
    return Module<InProc>::GetModule().GetObjectCount() == 0 ? S_OK : S_FALSE;
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH) DisableThreadLibraryCalls(instance);
    return TRUE;
}
