import { useCallback, useEffect, useMemo, useState } from 'react';
import { fetchCurrentUser, logout as logoutRequest } from '../api/auth';
import { CurrentUserContext } from './currentUserContext';

// Asks the API once, when the site opens, who is logged in, and shares the answer with every page.
function CurrentUserProvider({ children }) {
    const [session, setSession] = useState({ status: 'loading', user: null });

    useEffect(() => {
        const controller = new AbortController();
        fetchCurrentUser(controller.signal)
            .then(user => {
                if (!controller.signal.aborted) {
                    setSession(user ? { status: 'authenticated', user } : { status: 'anonymous', user: null });
                }
            })
            .catch(error => {
                if (!controller.signal.aborted) {
                    setSession({ status: 'error', user: null, message: error.message });
                }
            });

        return () => controller.abort();
    }, []);

    const logout = useCallback(async () => {
        await logoutRequest();
        setSession({ status: 'anonymous', user: null });
    }, []);

    // Same object between renders unless the session changes, so consumers don't re-render for nothing.
    const value = useMemo(() => ({ ...session, logout }), [session, logout]);

    return <CurrentUserContext.Provider value={value}>{children}</CurrentUserContext.Provider>;
}

export default CurrentUserProvider;
