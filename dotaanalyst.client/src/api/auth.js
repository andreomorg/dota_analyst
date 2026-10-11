// The session lives in an HttpOnly cookie: the browser sends it on every same-origin request
// and JavaScript can't read it. So there is no token here — only calls that rely on that cookie.

// Asks the API who is logged in. Resolves to the user, or to null when nobody is (401).
// Network/HTTP failures become errors with a Portuguese message, like in matches.js.
export async function fetchCurrentUser(signal) {
    let response;
    try {
        response = await fetch('/api/v1/me', { signal });
    } catch (error) {
        if (signal?.aborted) {
            throw error;
        }
        throw new Error('Não foi possível conectar à API.', { cause: error });
    }

    if (response.status === 401) {
        return null;
    }

    if (!response.ok) {
        throw new Error(
            response.status === 429
                ? 'Muitas requisições em pouco tempo. Espere um minuto e tente de novo.'
                : 'Erro inesperado ao verificar o login.',
        );
    }

    try {
        return await response.json();
    } catch (error) {
        if (signal?.aborted) {
            throw error;
        }
        throw new Error('Erro inesperado ao verificar o login.', { cause: error });
    }
}

// Ends the session (the API deletes the cookie). Also succeeds when there was no session.
export async function logout() {
    let response;
    try {
        response = await fetch('/api/v1/auth/logout', { method: 'POST' });
    } catch (error) {
        throw new Error('Não foi possível conectar à API.', { cause: error });
    }

    if (!response.ok) {
        throw new Error('Não foi possível sair. Tente de novo.');
    }
}

// The login is a full-page navigation (Steam's page, then back here), never a fetch.
// returnUrl must be a local path ("/matches/123"); the API sends anything else to "/".
export function loginUrl(returnUrl) {
    return `/api/v1/auth/login?returnUrl=${encodeURIComponent(returnUrl)}`;
}
