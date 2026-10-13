import { useState } from 'react';
import { Link, useLocation } from 'react-router';
import { loginUrl } from '../api/auth';
import { useCurrentUser } from '../auth/useCurrentUser';

function SiteHeader() {
    const { status } = useCurrentUser();

    return (
        <header className="site-header">
            <Link to="/" className="site-name">
                <span className="site-mark" aria-hidden="true">D</span>
                <span className="site-brand-copy">
                    <span>DotaAnalyst</span>
                    <span className="site-tagline">Histórico de partidas</span>
                </span>
            </Link>
            {status === 'authenticated' ? <UserMenu /> : status !== 'loading' && <LoginButton />}
        </header>
    );
}

function LoginButton() {
    const location = useLocation();
    // Comes back to the page the user is on after logging in at Steam.
    const href = loginUrl(location.pathname + location.search);

    return (
        <div className="site-login">
            {/* A link, not a fetch: the login leaves the site, goes to Steam's page and comes back. */}
            <a className="steam-login" href={href}>Entrar com a Steam</a>
            <p className="site-login-hint">
                Dá para entrar com o QR code no app da Steam. O site nunca vê a sua senha.
            </p>
        </div>
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
            {user.avatarUrl && <img className="site-avatar" src={user.avatarUrl} alt="" width="32" height="32" />}
            <span className="site-user-name">
                Conectado como <strong>{name}</strong>
            </span>
            <button type="button" className="logout-button" onClick={handleLogout} disabled={logoutState.status === 'pending'}>
                Sair
            </button>
            {logoutState.status === 'error' && (
                <p className="error-message site-user-error" role="alert">{logoutState.message}</p>
            )}
        </div>
    );
}

export default SiteHeader;
