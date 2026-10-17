#include <windows.h>
#include <wincrypt.h>
#include <sddl.h>
#include <aclapi.h>
#include "CredentialVault.h"
#include "BridgeProtocol.h"
namespace Vault {
namespace {
bool Path(PCWSTR sid, wchar_t (&path)[512]) { return sid && wcslen(sid) <= 183 && !wcspbrk(sid, L"\\/") && swprintf_s(path, L"%s\\%s", Root, sid) > 0; }
bool Restricted(HKEY key) {
    PSECURITY_DESCRIPTOR security = nullptr; PACL acl = nullptr;
    if (GetSecurityInfo(key, SE_REGISTRY_KEY, DACL_SECURITY_INFORMATION | OWNER_SECURITY_INFORMATION, nullptr, nullptr, &acl, nullptr, &security) != ERROR_SUCCESS) return false;
    SECURITY_DESCRIPTOR_CONTROL control{}; DWORD revision = 0; bool valid = GetSecurityDescriptorControl(security, &control, &revision) && (control & SE_DACL_PROTECTED) && acl && acl->AceCount == 2;
    PSID owner = nullptr; BOOL defaultOwner = FALSE;
    valid = valid && GetSecurityDescriptorOwner(security, &owner, &defaultOwner) && owner && (IsWellKnownSid(owner, WinLocalSystemSid) || IsWellKnownSid(owner, WinBuiltinAdministratorsSid));
    bool system = false, admins = false;
    for (DWORD index = 0; valid && index < acl->AceCount; ++index) {
        void* entry = nullptr; valid = GetAce(acl, index, &entry) != FALSE;
        if (!valid) break;
        auto ace = static_cast<ACCESS_ALLOWED_ACE*>(entry);
        valid = ace->Header.AceType == ACCESS_ALLOWED_ACE_TYPE && !(ace->Header.AceFlags & INHERITED_ACE) && (ace->Mask & KEY_ALL_ACCESS) == KEY_ALL_ACCESS;
        auto sid = &ace->SidStart;
        if (IsWellKnownSid(sid, WinLocalSystemSid)) system = true;
        else if (IsWellKnownSid(sid, WinBuiltinAdministratorsSid)) admins = true;
        else valid = false;
    }
    LocalFree(security); return valid && system && admins;
}
void Marker(PCWSTR sid, DWORD enabled) {
    wchar_t path[512]{}; if (!sid || swprintf_s(path, L"%s%s", Bridge::BindingRoot, sid) < 0) return;
    HKEY key = nullptr;
    if (RegOpenKeyExW(HKEY_LOCAL_MACHINE, path, 0, KEY_SET_VALUE | KEY_WOW64_64KEY, &key) == ERROR_SUCCESS) { RegSetValueExW(key, L"Automatic", 0, REG_DWORD, reinterpret_cast<const BYTE*>(&enabled), sizeof(enabled)); RegCloseKey(key); }
}
}
bool Valid(const Record& record) {
    constexpr wchar_t prefix[] = L"MicrosoftAccount\\";
    return record.magic == 0x41535631 && record.version == 1 && record.size == sizeof(Record) && !record.reserved && record.phone && wmemchr(record.sid, 0, _countof(record.sid)) && wcsncmp(record.sid, L"S-1-", 4) == 0 && !wcspbrk(record.sid, L"\\/") && wmemchr(record.qualified, 0, _countof(record.qualified)) && _wcsnicmp(record.qualified, prefix, _countof(prefix) - 1) == 0 && record.qualified[_countof(prefix) - 1] && wmemchr(record.password, 0, _countof(record.password)) && record.password[0];
}
bool Enabled(PCWSTR sid) {
    wchar_t path[512]{}; if (!sid || swprintf_s(path, L"%s%s", Bridge::BindingRoot, sid) < 0) return false;
    DWORD flag = 0, bytes = sizeof(flag);
    return RegGetValueW(HKEY_LOCAL_MACHINE, path, L"Automatic", RRF_RT_REG_DWORD | RRF_SUBKEY_WOW6464KEY, nullptr, &flag, &bytes) == ERROR_SUCCESS && flag == 1;
}
bool Encrypt(const Record& record, BYTE** data, DWORD* bytes) {
    *data = nullptr; *bytes = 0; if (!Valid(record)) return false;
    DATA_BLOB input{sizeof(record), reinterpret_cast<BYTE*>(const_cast<Record*>(&record))}, output{};
    if (!CryptProtectData(&input, L"AudioSwitch local sign-in", nullptr, nullptr, nullptr, CRYPTPROTECT_LOCAL_MACHINE | CRYPTPROTECT_UI_FORBIDDEN, &output)) return false;
    *data = output.pbData; *bytes = output.cbData; return true;
}
bool Decrypt(const BYTE* data, DWORD bytes, Record& record) {
    SecureZeroMemory(&record, sizeof(record));
    if (!data || !bytes || bytes > 16384) return false;
    DATA_BLOB input{bytes, const_cast<BYTE*>(data)}, output{};
    if (!CryptUnprotectData(&input, nullptr, nullptr, nullptr, nullptr, CRYPTPROTECT_UI_FORBIDDEN, &output)) return false;
    const bool sized = output.cbData == sizeof(record);
    if (sized) memcpy(&record, output.pbData, sizeof(record));
    SecureZeroMemory(output.pbData, output.cbData); LocalFree(output.pbData);
    if (!sized || !Valid(record)) { SecureZeroMemory(&record, sizeof(record)); return false; } return true;
}
bool Save(const Record& record) {
    uint64_t phone = 0; if (!Valid(record) || !Bridge::ReadBinding(record.sid, &phone) || phone != record.phone) return false;
    BYTE* encrypted = nullptr; DWORD bytes = 0; if (!Encrypt(record, &encrypted, &bytes)) return false;
    PSECURITY_DESCRIPTOR descriptor = nullptr; HKEY root = nullptr, key = nullptr;
    bool valid = ConvertStringSecurityDescriptorToSecurityDescriptorW(L"O:BAG:SYD:P(A;;KA;;;SY)(A;;KA;;;BA)", SDDL_REVISION_1, &descriptor, nullptr) != FALSE;
    SECURITY_ATTRIBUTES attributes{sizeof(attributes), descriptor, FALSE};
    wchar_t path[512]{}; valid = valid && Path(record.sid, path);
    if (valid) valid = RegCreateKeyExW(HKEY_LOCAL_MACHINE, Root, 0, nullptr, 0, KEY_ALL_ACCESS | KEY_WOW64_64KEY, &attributes, &root, nullptr) == ERROR_SUCCESS;
    if (valid) valid = RegSetKeySecurity(root, DACL_SECURITY_INFORMATION | PROTECTED_DACL_SECURITY_INFORMATION, descriptor) == ERROR_SUCCESS && Restricted(root);
    if (valid) valid = RegCreateKeyExW(HKEY_LOCAL_MACHINE, path, 0, nullptr, 0, KEY_ALL_ACCESS | KEY_WOW64_64KEY, &attributes, &key, nullptr) == ERROR_SUCCESS;
    if (valid) valid = RegSetKeySecurity(key, DACL_SECURITY_INFORMATION | PROTECTED_DACL_SECURITY_INFORMATION, descriptor) == ERROR_SUCCESS && Restricted(key);
    if (valid) valid = RegSetValueExW(key, L"Secret", 0, REG_BINARY, encrypted, bytes) == ERROR_SUCCESS;
    if (key) RegCloseKey(key); if (root) RegCloseKey(root); if (descriptor) LocalFree(descriptor);
    SecureZeroMemory(encrypted, bytes); LocalFree(encrypted);
    if (valid) { Marker(record.sid, 1); valid = Enabled(record.sid); } return valid;
}
bool Load(PCWSTR sid, PCWSTR qualified, Record& record) {
    if (!Enabled(sid)) return false;
    wchar_t path[512]{}; if (!Path(sid, path)) return false;
    HKEY key = nullptr; if (RegOpenKeyExW(HKEY_LOCAL_MACHINE, path, 0, KEY_QUERY_VALUE | READ_CONTROL | KEY_WOW64_64KEY, &key) != ERROR_SUCCESS) return false;
    BYTE encrypted[16384]{}; DWORD bytes = sizeof(encrypted), type = 0;
    const bool loaded = Restricted(key) && RegQueryValueExW(key, L"Secret", nullptr, &type, encrypted, &bytes) == ERROR_SUCCESS && type == REG_BINARY && Decrypt(encrypted, bytes, record);
    RegCloseKey(key); SecureZeroMemory(encrypted, sizeof(encrypted));
    uint64_t phone = 0;
    const bool valid = loaded && wcscmp(record.sid, sid) == 0 && (!qualified || _wcsicmp(record.qualified, qualified) == 0) && Bridge::ReadBinding(sid, &phone) && record.phone == phone;
    if (!valid) SecureZeroMemory(&record, sizeof(record)); return valid;
}
void Revoke(PCWSTR sid) {
    Marker(sid, 0); wchar_t path[512]{}; if (!Path(sid, path)) return;
    HKEY key = nullptr;
    if (RegOpenKeyExW(HKEY_LOCAL_MACHINE, path, 0, KEY_SET_VALUE | KEY_WOW64_64KEY, &key) == ERROR_SUCCESS) { RegDeleteValueW(key, L"Secret"); RegCloseKey(key); }
    RegDeleteKeyExW(HKEY_LOCAL_MACHINE, path, KEY_WOW64_64KEY, 0);
}
}
