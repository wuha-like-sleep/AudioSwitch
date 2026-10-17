#pragma once
#include <windows.h>
#include <cstdint>
namespace Vault {
inline constexpr wchar_t Root[] = L"SOFTWARE\\AudioSwitch\\LoginBridge\\Vault";
struct Record {
    DWORD magic = 0x41535631, version = 1, size = sizeof(Record), reserved = 0;
    uint64_t phone = 0;
    wchar_t sid[184]{}, qualified[512]{}, password[1025]{};
    ~Record() { SecureZeroMemory(this, sizeof(*this)); }
};
bool Valid(const Record& record);
bool Enabled(PCWSTR sid);
bool Save(const Record& record);
bool Load(PCWSTR sid, PCWSTR qualified, Record& record);
void Revoke(PCWSTR sid);
bool Encrypt(const Record& record, BYTE** data, DWORD* bytes);
bool Decrypt(const BYTE* data, DWORD bytes, Record& record);
}
