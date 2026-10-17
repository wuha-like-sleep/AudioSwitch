// Copyright (c) 2026 wuha-like-sleep. Licensed under the MIT license.
#define SECURITY_WIN32
#include <windows.h>
#include <credentialprovider.h>
#include <ntsecapi.h>
#include <wincred.h>
#include <propkey.h>
#include <shlwapi.h>
#include <wrl/client.h>
#include <array>
#include <new>
#include "ProviderId.h"
#include "BridgeClient.h"
#include "CredentialVault.h"
#include <thread>
#include <mutex>
using Microsoft::WRL::ComPtr;
namespace {
LONG objects = 0, locks = 0;
HINSTANCE moduleInstance = nullptr;
constexpr DWORD FieldCount = 4, PasswordField = 2;
HRESULT CopyText(PCWSTR text, PWSTR* output) {
    if (!output) return E_POINTER;
    *output = nullptr;
    if (!text) return E_INVALIDARG;
    const size_t bytes = (wcslen(text) + 1) * sizeof(wchar_t);
    auto copy = static_cast<PWSTR>(CoTaskMemAlloc(bytes));
    if (!copy) return E_OUTOFMEMORY;
    memcpy(copy, text, bytes); *output = copy; return S_OK;
}
struct Secret {
    PWSTR text = nullptr;
    ~Secret() { Clear(); }
    void Clear() { if (text) { SecureZeroMemory(text, (wcslen(text) + 1) * sizeof(wchar_t)); CoTaskMemFree(text); text = nullptr; } }
    HRESULT Set(PCWSTR value) { PWSTR next = nullptr; auto hr = CopyText(value, &next); if (SUCCEEDED(hr)) { Clear(); text = next; } return hr; }
};
HRESULT NegotiatePackage(ULONG* id) {
    LSA_HANDLE handle = nullptr;
    NTSTATUS status = LsaConnectUntrusted(&handle);
    if (status < 0) return HRESULT_FROM_WIN32(LsaNtStatusToWinError(status));
    char name[] = "Negotiate";
    LSA_STRING package{static_cast<USHORT>(sizeof(name) - 1), sizeof(name), name};
    status = LsaLookupAuthenticationPackage(handle, &package, id);
    LsaDeregisterLogonProcess(handle);
    return status < 0 ? HRESULT_FROM_WIN32(LsaNtStatusToWinError(status)) : S_OK;
}
class Credential final : public ICredentialProviderCredential2 {
    LONG refs = 1;
    PWSTR sid = nullptr, qualified = nullptr;
    Secret password;
    PhoneApproval automaticApproval{};
    bool autoReady = false, automaticSubmitted = false, manualEditing = false;
    ComPtr<ICredentialProviderCredentialEvents> events;
public:
    PCWSTR Sid() const { return sid; }
    bool AutomaticReady() const { const auto now = GetTickCount64(); return autoReady && !manualEditing && Vault::Enabled(sid) && now >= automaticApproval.completed && now - automaticApproval.completed <= 2000; }
    void Ready(const PhoneApproval& approval) { if (!manualEditing) { automaticApproval = approval; autoReady = true; } }
    Credential() { InterlockedIncrement(&objects); }
    ~Credential() { CoTaskMemFree(sid); CoTaskMemFree(qualified); InterlockedDecrement(&objects); }
    HRESULT Initialize(ICredentialProviderUser* user) {
        auto hr = user->GetStringValue(PKEY_Identity_QualifiedUserName, &qualified);
        if (FAILED(hr)) return hr;
        // This lab supports only the explicit MicrosoftAccount identity namespace.
        // Local, domain, Entra and unknown providers must use their existing sign-in options.
        constexpr wchar_t prefix[] = L"MicrosoftAccount\\";
        if (!qualified || _wcsnicmp(qualified, prefix, _countof(prefix) - 1) != 0 || !qualified[_countof(prefix) - 1]) return HRESULT_FROM_WIN32(ERROR_NOT_SUPPORTED);
        hr = user->GetSid(&sid);
        return SUCCEEDED(hr) && (!sid || !*sid) ? E_INVALIDARG : hr;
    }
    IFACEMETHODIMP QueryInterface(REFIID iid, void** result) override {
        if (!result) return E_POINTER; *result = nullptr;
        if (iid == IID_IUnknown || iid == IID_ICredentialProviderCredential || iid == IID_ICredentialProviderCredential2) *result = static_cast<ICredentialProviderCredential2*>(this);
        else return E_NOINTERFACE;
        AddRef(); return S_OK;
    }
    IFACEMETHODIMP_(ULONG) AddRef() override { return InterlockedIncrement(&refs); }
    IFACEMETHODIMP_(ULONG) Release() override { const auto n = InterlockedDecrement(&refs); if (!n) delete this; return n; }
    IFACEMETHODIMP Advise(ICredentialProviderCredentialEvents* value) override { events = value; return S_OK; }
    IFACEMETHODIMP UnAdvise() override { events.Reset(); return S_OK; }
    IFACEMETHODIMP SetSelected(BOOL* automatic) override { if (!automatic) return E_POINTER; *automatic = FALSE; return S_OK; }
    IFACEMETHODIMP SetDeselected() override {
        password.Clear(); manualEditing = false;
        if (events) events->SetFieldString(this, PasswordField, L"");
        return S_OK;
    }
    IFACEMETHODIMP GetFieldState(DWORD id, CREDENTIAL_PROVIDER_FIELD_STATE* state, CREDENTIAL_PROVIDER_FIELD_INTERACTIVE_STATE* interaction) override {
        if (!state || !interaction) return E_POINTER;
        if (id >= FieldCount) return E_INVALIDARG;
        *state = id == 0 ? CPFS_DISPLAY_IN_BOTH : CPFS_DISPLAY_IN_SELECTED_TILE;
        *interaction = id == PasswordField ? CPFIS_FOCUSED : CPFIS_NONE; return S_OK;
    }
    IFACEMETHODIMP GetStringValue(DWORD id, PWSTR* text) override {
        if (!text) return E_POINTER; *text = nullptr;
        switch (id) {
        case 0: return CopyText(L"AudioSwitch 登录实验", text);
        case 1: return CopyText(L"已初始化时可在手机恢复验证后自动提交；PIN / Hello 继续使用系统入口。", text);
        case 2: return CopyText(password.text ? password.text : L"", text);
        case 3: return CopyText(L"登录", text);
        default: return E_INVALIDARG;
        }
    }
    IFACEMETHODIMP GetBitmapValue(DWORD, HBITMAP* value) override { if (value) *value = nullptr; return E_NOTIMPL; }
    IFACEMETHODIMP GetCheckboxValue(DWORD, BOOL*, PWSTR* label) override { if (label) *label = nullptr; return E_NOTIMPL; }
    IFACEMETHODIMP GetComboBoxValueCount(DWORD, DWORD*, DWORD*) override { return E_NOTIMPL; }
    IFACEMETHODIMP GetComboBoxValueAt(DWORD, DWORD, PWSTR* text) override { if (text) *text = nullptr; return E_NOTIMPL; }
    IFACEMETHODIMP GetSubmitButtonValue(DWORD id, DWORD* adjacent) override { if (!adjacent) return E_POINTER; if (id != 3) return E_INVALIDARG; *adjacent = PasswordField; return S_OK; }
    IFACEMETHODIMP SetStringValue(DWORD id, PCWSTR value) override {
        if (id != PasswordField || !value) return E_INVALIDARG;
        if (wcslen(value) > 1024) return E_INVALIDARG;
        manualEditing = *value != 0; autoReady = false;
        return password.Set(value);
    }
    IFACEMETHODIMP SetCheckboxValue(DWORD, BOOL) override { return E_NOTIMPL; }
    IFACEMETHODIMP SetComboBoxSelectedValue(DWORD, DWORD) override { return E_NOTIMPL; }
    IFACEMETHODIMP CommandLinkClicked(DWORD) override { return E_NOTIMPL; }
    IFACEMETHODIMP GetUserSid(PWSTR* value) override { return CopyText(sid, value); }
    IFACEMETHODIMP GetSerialization(CREDENTIAL_PROVIDER_GET_SERIALIZATION_RESPONSE* response, CREDENTIAL_PROVIDER_CREDENTIAL_SERIALIZATION* serialized, PWSTR* status, CREDENTIAL_PROVIDER_STATUS_ICON* icon) override {
        if (!response || !serialized || !status || !icon) return E_POINTER;
        *response = CPGSR_NO_CREDENTIAL_NOT_FINISHED; *serialized = {}; *status = nullptr; *icon = CPSI_NONE;
        const bool automatic = AutomaticReady() && (!password.text || !*password.text);
        autoReady = false; automaticSubmitted = false;
        Vault::Record saved;
        if (automatic) {
            if (!CommitAutomatic(sid, automaticApproval)) { SetDeselected(); *icon = CPSI_WARNING; return CopyText(L"手机证明已失效，未提交凭据。可继续使用 PIN / Windows Hello。", status); }
            if (!Vault::Load(sid, qualified, saved) || FAILED(password.Set(saved.password))) {
                Vault::Revoke(sid); SetDeselected(); *icon = CPSI_WARNING; return CopyText(L"本机凭据不可用，已停用自动提交。请使用 PIN / Windows Hello 并重新初始化。", status);
            }
            automaticSubmitted = true;
        }
        if (!password.text || !*password.text) { *icon = CPSI_WARNING; return CopyText(L"请输入微软账户密码；请勿输入 PIN。", status); }
        if (!automatic && !VerifyBoundPhone(sid)) {
            SetDeselected(); *icon = CPSI_WARNING;
            return CopyText(L"未取得绑定手机的新鲜认证结果，已停止提交并清除密码。请检查手机和 AudioSwitch 登录桥接服务，或切回系统 PIN / Windows Hello。", status);
        }
        ULONG package = 0;
        auto hr = NegotiatePackage(&package);
        if (FAILED(hr)) { SetDeselected(); return hr; }
        if (automatic) {
            uint64_t currentPhone = 0; const auto now = GetTickCount64();
            if (!Vault::Enabled(sid) || !Bridge::ReadBinding(sid, &currentPhone) || currentPhone != saved.phone || now < automaticApproval.completed || now - automaticApproval.completed > 2000) {
                SetDeselected(); automaticSubmitted = false; *icon = CPSI_WARNING;
                return CopyText(L"自动提交已撤销或手机证明过期，未提交凭据。可继续使用系统登录选项。", status);
            }
        }
        const DWORD flags = CRED_PACK_PROTECTED_CREDENTIALS | CRED_PACK_ID_PROVIDER_CREDENTIALS;
        DWORD bytes = 0;
        const BOOL sized = CredPackAuthenticationBufferW(flags, qualified, password.text, nullptr, &bytes);
        const DWORD sizeError = GetLastError();
        if (sized || sizeError != ERROR_INSUFFICIENT_BUFFER || !bytes) { SetDeselected(); return HRESULT_FROM_WIN32(sizeError ? sizeError : ERROR_INVALID_DATA); }
        auto buffer = static_cast<BYTE*>(CoTaskMemAlloc(bytes));
        if (!buffer) { SetDeselected(); return E_OUTOFMEMORY; }
        const BOOL packed = CredPackAuthenticationBufferW(flags, qualified, password.text, buffer, &bytes);
        const DWORD error = GetLastError();
        SetDeselected(); // No stored password, including between retries.
        if (!packed) { SecureZeroMemory(buffer, bytes); CoTaskMemFree(buffer); return HRESULT_FROM_WIN32(error ? error : ERROR_INVALID_DATA); }
        serialized->ulAuthenticationPackage = package;
        serialized->clsidCredentialProvider = AudioSwitchLoginProviderId;
        serialized->cbSerialization = bytes;
        serialized->rgbSerialization = buffer;
        *response = CPGSR_RETURN_CREDENTIAL_FINISHED;
        return S_OK; // LogonUI owns the buffer and Windows decides whether it authenticates.
    }
    IFACEMETHODIMP ReportResult(NTSTATUS statusCode, NTSTATUS, PWSTR* status, CREDENTIAL_PROVIDER_STATUS_ICON* icon) override {
        if (!status || !icon) return E_POINTER;
        *status = nullptr; *icon = CPSI_NONE; SetDeselected();
        if (automaticSubmitted && statusCode < 0) Vault::Revoke(sid);
        automaticSubmitted = false;
        if (statusCode < 0) { *icon = CPSI_ERROR; return CopyText(L"Windows 未接受该凭据。请使用其他登录选项中的 PIN / Windows Hello。", status); }
        return S_OK;
    }
};
class Provider final : public ICredentialProvider, public ICredentialProviderSetUserArray {
    LONG refs = 1;
    CREDENTIAL_PROVIDER_USAGE_SCENARIO scenario = CPUS_INVALID;
    ComPtr<ICredentialProviderUserArray> users;
    std::array<ComPtr<Credential>, 64> credentials;
    DWORD count = 0;
    bool enumerated = false;
    ComPtr<ICredentialProviderEvents> providerEvents;
    UINT_PTR adviseContext = 0;
    HWND notification = nullptr; HANDLE stop = nullptr;
    std::thread worker;
    struct Target { wchar_t sid[184]{}; DWORD index = 0; };
    std::mutex pendingLock;
    bool pending = false; DWORD readyIndex = 0; PhoneApproval readyProof{};
    static LRESULT CALLBACK WindowProc(HWND window, UINT message, WPARAM w, LPARAM l) {
        if (message == WM_NCCREATE) SetWindowLongPtrW(window, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(reinterpret_cast<CREATESTRUCTW*>(l)->lpCreateParams));
        auto self = reinterpret_cast<Provider*>(GetWindowLongPtrW(window, GWLP_USERDATA));
        if (self && message == WM_APP + 71) { self->NotifyReady(); return 0; }
        return DefWindowProcW(window, message, w, l);
    }
    void NotifyReady() {
        DWORD index; PhoneApproval proof;
        { std::lock_guard<std::mutex> guard(pendingLock); if (!pending) return; index = readyIndex; proof = readyProof; pending = false; }
        if (index < count && providerEvents) { credentials[index]->Ready(proof); if (credentials[index]->AutomaticReady()) providerEvents->CredentialsChanged(adviseContext); }
    }
    void StopWorker() {
        if (stop) SetEvent(stop); if (worker.joinable()) worker.join();
        if (notification) DestroyWindow(notification); notification = nullptr;
        if (stop) CloseHandle(stop); stop = nullptr;
        UnregisterClassW(L"AudioSwitch.LoginProvider.Events", moduleInstance);
        std::lock_guard<std::mutex> guard(pendingLock); pending = false;
    }
    void StartWorker() {
        if (worker.joinable() || !enumerated || !count || !providerEvents) return;
        std::array<Target, 64> targets{}; const DWORD targetCount = count;
        for (DWORD index = 0; index < count; ++index) { wcsncpy_s(targets[index].sid, credentials[index]->Sid(), _TRUNCATE); targets[index].index = index; }
        WNDCLASSW definition{}; definition.lpfnWndProc = WindowProc; definition.hInstance = moduleInstance; definition.lpszClassName = L"AudioSwitch.LoginProvider.Events";
        if (!RegisterClassW(&definition) && GetLastError() != ERROR_CLASS_ALREADY_EXISTS) return;
        notification = CreateWindowExW(0, definition.lpszClassName, L"", 0, 0, 0, 0, 0, HWND_MESSAGE, nullptr, moduleInstance, this);
        stop = CreateEventW(nullptr, TRUE, FALSE, nullptr);
        if (!notification || !stop) { StopWorker(); return; }
        try {
            worker = std::thread([this, targets, targetCount] {
                while (WaitForSingleObject(stop, 0) != WAIT_OBJECT_0) {
                    for (DWORD index = 0; index < targetCount && WaitForSingleObject(stop, 0) != WAIT_OBJECT_0; ++index) {
                        if (!Vault::Enabled(targets[index].sid)) continue;
                        PhoneApproval proof;
                        if (ProbeAutomatic(targets[index].sid, proof, stop)) {
                            { std::lock_guard<std::mutex> guard(pendingLock); readyIndex = targets[index].index; readyProof = proof; pending = true; }
                            PostMessageW(notification, WM_APP + 71, 0, 0);
                        }
                    }
                    if (WaitForSingleObject(stop, 3000) == WAIT_OBJECT_0) break;
                }
            });
        } catch (...) { StopWorker(); }
    }
    void Clear() { StopWorker(); for (auto& value : credentials) value.Reset(); count = 0; enumerated = false; }
    HRESULT Enumerate() {
        if (enumerated) return S_OK;
        if (!users || (scenario != CPUS_LOGON && scenario != CPUS_UNLOCK_WORKSTATION)) return S_OK;
        DWORD total = 0; auto hr = users->GetCount(&total); if (FAILED(hr)) return hr;
        if (total > credentials.size()) return HRESULT_FROM_WIN32(ERROR_TOO_MANY_NAMES);
        for (DWORD index = 0; index < total; ++index) {
            ComPtr<ICredentialProviderUser> user;
            hr = users->GetAt(index, &user); if (FAILED(hr)) { Clear(); return hr; }
            ComPtr<Credential> credential; credential.Attach(new (std::nothrow) Credential());
            if (!credential) { Clear(); return E_OUTOFMEMORY; }
            hr = credential->Initialize(user.Get());
            if (hr == HRESULT_FROM_WIN32(ERROR_NOT_SUPPORTED)) continue;
            if (FAILED(hr)) { Clear(); return hr; }
            credentials[count++] = credential;
        }
        enumerated = true; StartWorker(); return S_OK;
    }
public:
    Provider() { InterlockedIncrement(&objects); }
    ~Provider() { Clear(); InterlockedDecrement(&objects); }
    IFACEMETHODIMP QueryInterface(REFIID iid, void** result) override {
        if (!result) return E_POINTER; *result = nullptr;
        if (iid == IID_IUnknown || iid == IID_ICredentialProvider) *result = static_cast<ICredentialProvider*>(this);
        else if (iid == IID_ICredentialProviderSetUserArray) *result = static_cast<ICredentialProviderSetUserArray*>(this);
        else return E_NOINTERFACE;
        AddRef(); return S_OK;
    }
    IFACEMETHODIMP_(ULONG) AddRef() override { return InterlockedIncrement(&refs); }
    IFACEMETHODIMP_(ULONG) Release() override { const auto n = InterlockedDecrement(&refs); if (!n) delete this; return n; }
    IFACEMETHODIMP SetUsageScenario(CREDENTIAL_PROVIDER_USAGE_SCENARIO value, DWORD) override {
        Clear(); scenario = CPUS_INVALID;
        if (value != CPUS_LOGON && value != CPUS_UNLOCK_WORKSTATION) return E_NOTIMPL;
        scenario = value; return S_OK;
    }
    IFACEMETHODIMP SetSerialization(const CREDENTIAL_PROVIDER_CREDENTIAL_SERIALIZATION*) override { return E_NOTIMPL; }
    IFACEMETHODIMP Advise(ICredentialProviderEvents* value, UINT_PTR context) override { StopWorker(); providerEvents = value; adviseContext = context; StartWorker(); return S_OK; }
    IFACEMETHODIMP UnAdvise() override { StopWorker(); providerEvents.Reset(); return S_OK; }
    IFACEMETHODIMP GetFieldDescriptorCount(DWORD* value) override { if (!value) return E_POINTER; *value = FieldCount; return S_OK; }
    IFACEMETHODIMP GetFieldDescriptorAt(DWORD id, CREDENTIAL_PROVIDER_FIELD_DESCRIPTOR** output) override {
        if (!output) return E_POINTER; *output = nullptr;
        if (id >= FieldCount) return E_INVALIDARG;
        constexpr CREDENTIAL_PROVIDER_FIELD_TYPE types[] = {CPFT_LARGE_TEXT, CPFT_SMALL_TEXT, CPFT_PASSWORD_TEXT, CPFT_SUBMIT_BUTTON};
        constexpr PCWSTR labels[] = {L"AudioSwitch 登录实验", L"实验范围", L"微软账户密码（不是 PIN）", L"登录"};
        auto descriptor = static_cast<CREDENTIAL_PROVIDER_FIELD_DESCRIPTOR*>(CoTaskMemAlloc(sizeof(CREDENTIAL_PROVIDER_FIELD_DESCRIPTOR)));
        if (!descriptor) return E_OUTOFMEMORY;
        *descriptor = {}; descriptor->dwFieldID = id; descriptor->cpft = types[id];
        auto hr = CopyText(labels[id], &descriptor->pszLabel);
        if (FAILED(hr)) { CoTaskMemFree(descriptor); return hr; }
        *output = descriptor; return S_OK;
    }
    IFACEMETHODIMP GetCredentialCount(DWORD* value, DWORD* defaultIndex, BOOL* automatic) override {
        if (!value || !defaultIndex || !automatic) return E_POINTER;
        *value = 0; *defaultIndex = CREDENTIAL_PROVIDER_NO_DEFAULT; *automatic = FALSE;
        auto hr = Enumerate();
        if (SUCCEEDED(hr)) { *value = count; for (DWORD index = 0; index < count; ++index) if (credentials[index]->AutomaticReady()) { *defaultIndex = index; *automatic = TRUE; break; } }
        return hr;
    }
    IFACEMETHODIMP GetCredentialAt(DWORD index, ICredentialProviderCredential** value) override {
        if (!value) return E_POINTER; *value = nullptr;
        auto hr = Enumerate(); if (FAILED(hr)) return hr;
        if (index >= count) return E_INVALIDARG;
        return credentials[index].CopyTo(value);
    }
    IFACEMETHODIMP SetUserArray(ICredentialProviderUserArray* value) override { Clear(); users = value; return S_OK; }
};
class Factory final : public IClassFactory {
    LONG refs = 1;
public:
    Factory() { InterlockedIncrement(&objects); }
    ~Factory() { InterlockedDecrement(&objects); }
    IFACEMETHODIMP QueryInterface(REFIID iid, void** value) override { if (!value) return E_POINTER; *value = nullptr; if (iid != IID_IUnknown && iid != IID_IClassFactory) return E_NOINTERFACE; *value = static_cast<IClassFactory*>(this); AddRef(); return S_OK; }
    IFACEMETHODIMP_(ULONG) AddRef() override { return InterlockedIncrement(&refs); }
    IFACEMETHODIMP_(ULONG) Release() override { auto n = InterlockedDecrement(&refs); if (!n) delete this; return n; }
    IFACEMETHODIMP CreateInstance(IUnknown* outer, REFIID iid, void** value) override {
        if (!value) return E_POINTER; *value = nullptr; if (outer) return CLASS_E_NOAGGREGATION;
        auto provider = new (std::nothrow) Provider(); if (!provider) return E_OUTOFMEMORY;
        auto hr = provider->QueryInterface(iid, value); provider->Release(); return hr;
    }
    IFACEMETHODIMP LockServer(BOOL lock) override { if (lock) InterlockedIncrement(&locks); else InterlockedDecrement(&locks); return S_OK; }
};
}
BOOL WINAPI DllMain(HINSTANCE module, DWORD reason, LPVOID) { if (reason == DLL_PROCESS_ATTACH) moduleInstance = module; return TRUE; }
extern "C" HRESULT __stdcall DllCanUnloadNow() { return objects == 0 && locks == 0 ? S_OK : S_FALSE; }
extern "C" HRESULT __stdcall DllGetClassObject(REFCLSID clsid, REFIID iid, void** value) {
    if (!value) return E_POINTER; *value = nullptr;
    if (clsid != AudioSwitchLoginProviderId) return CLASS_E_CLASSNOTAVAILABLE;
    auto factory = new (std::nothrow) Factory(); if (!factory) return E_OUTOFMEMORY;
    auto hr = factory->QueryInterface(iid, value); factory->Release(); return hr;
}
