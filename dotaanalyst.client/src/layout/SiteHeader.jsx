import { useState } from 'react';
import { Link, NavLink, useLocation } from 'react-router';
import { loginUrl } from '../api/auth';
import { useCurrentUser } from '../auth/useCurrentUser';

// A small chart line in a rounded square: the site's mark.
function Logo() {
    return (
        <svg className="site-logo" viewBox="0 0 32 32" aria-hidden="true">
            <rect x="1" y="1" width="30" height="30" rx="7" />
            <path d="M8 21l5-6 4 3 7-8" />
        </svg>
    );
}

function SiteHeader() {
    const { status } = useCurrentUser();

    return (
        <header className="site-header">
            <div className="site-header-inner">
                <div className="site-header-start">
                    <Link to="/" className="site-brand">
                        <Logo />
                        <span>DotaAnalyst</span>
                    </Link>
                    <nav className="site-nav" aria-label="Principal">
                        <NavLink to="/" end>Partidas</NavLink>
                    </nav>
                </div>
                {status === 'authenticated' ? <UserMenu /> : status !== 'loading' && <LoginButton />}
            </div>
        </header>
    );
}

function LoginButton() {
    const location = useLocation();
    // Comes back to the page the user is on after logging in at Steam.
    const href = loginUrl(location.pathname + location.search);

    return (
        // A link, not a fetch: the login leaves the site, goes to Steam's page and comes back.
        <a
            className="steam-login"
            href={href}
            title="Dá para entrar com o QR code no app da Steam. O site nunca vê a sua senha."
        >
            <svg viewBox="0 0 24 24" aria-hidden="true">
                <path d="M7 9h3M8.5 7.5v3M15 9h.01M17 11h.01M6.5 5h11A3.5 3.5 0 0 1 21 8.5v5a3.5 3.5 0 0 1-6.2 2.2l-.8-.7h-4l-.8.7A3.5 3.5 0 0 1 3 13.5v-5A3.5 3.5 0 0 1 6.5 5z" />
            </svg>
            Entrar com Steam
            <span className="visually-hidden"> (pela página da Steam, com senha ou QR code; o site nunca vê a sua senha)</span>
        </a>
    );
}

function UserMenu() {
    const { user, logout } = useCurrentUser();
    const [logoutState, setLogoutState] = useState({ status: 'idle' });
    const name = user.personaName ?? user.accountId;

    function handleLogout() {
        setLogoutState({ status: 'pending' });
        logout().catch(error => setLogoutState({ status: 'error', message: error.message }));
    }

    return (
        <div className="site-user">
            {user.avatarUrl
                ? <img className="site-avatar" src={user.avatarUrl} alt="" width="32" height="32" />
                : <span className="site-avatar site-avatar-empty" aria-hidden="true" />}
            <span className="site-user-name">
                <span className="visually-hidden">Conectado como </span>
                <strong>{name}</strong>
            </span>
            <button type="button" className="button-secondary" onClick={handleLogout} disabled={logoutState.status === 'pending'}>
                Sair
            </button>
            {logoutState.status === 'error' && (
                <p className="error-message site-user-error" role="alert">{logoutState.message}</p>
            )}
        </div>
    );
}

export default SiteHeader;
