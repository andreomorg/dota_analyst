import { useContext } from 'react';
import { CurrentUserContext } from './currentUserContext';

// Returns { status, user, logout }:
// - status: 'loading' (asking the API), 'anonymous', 'authenticated' or 'error' (couldn't check;
//   pages treat it like 'anonymous');
// - user: { accountId, steamId64, personaName, avatarUrl } when authenticated, otherwise null;
// - logout(): ends the session; rejects with a Portuguese message if the API fails.
export function useCurrentUser() {
    const value = useContext(CurrentUserContext);
    if (value === null) {
        throw new Error('useCurrentUser must be used inside <CurrentUserProvider>.');
    }
    return value;
}
