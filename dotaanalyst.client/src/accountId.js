// SteamID64 = account ID + this offset (same rule as AccountId in the API's Domain).
const STEAM_ID64_OFFSET = 76561197960265728n;

// Turns what the user typed (account ID or SteamID64) into the account ID, as text, or null when it
// can't be one. Only for comparing IDs in the page; the API still validates every request.
export function toAccountId(value) {
    if (!/^\d{1,17}$/.test(value ?? '')) {
        return null;
    }

    // BigInt: a SteamID64 is larger than JavaScript numbers can hold exactly.
    let id = BigInt(value);
    if (id >= STEAM_ID64_OFFSET) {
        id -= STEAM_ID64_OFFSET;
    }
    return id > 0n && id <= 4294967295n ? id.toString() : null;
}
