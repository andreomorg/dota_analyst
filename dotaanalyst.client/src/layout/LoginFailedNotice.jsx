import { useEffect, useState } from 'react';
import { useLocation, useNavigate } from 'react-router';

// The API sends the browser to "/?login=failed" when the Steam login fails or is cancelled.
function LoginFailedNotice() {
    const location = useLocation();
    const navigate = useNavigate();
    // Read once, when the site opens: the query string is removed right below.
    const [visible, setVisible] = useState(() => new URLSearchParams(location.search).get('login') === 'failed');

    useEffect(() => {
        const params = new URLSearchParams(location.search);
        if (params.get('login') !== 'failed') {
            return;
        }

        // Replaces the history entry, so refreshing the page doesn't show the message again.
        params.delete('login');
        const search = params.toString();
        navigate({ pathname: location.pathname, search: search ? `?${search}` : '', hash: location.hash }, { replace: true });
    }, [location, navigate]);

    if (!visible) {
        return null;
    }

    return (
        <div className="login-failed" role="alert">
            <p>Não foi possível entrar com a Steam. Tente de novo.</p>
            <button type="button" onClick={() => setVisible(false)} aria-label="Fechar aviso">×</button>
        </div>
    );
}

export default LoginFailedNotice;
