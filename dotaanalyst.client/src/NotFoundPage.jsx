import { Link } from 'react-router';

function NotFoundPage() {
    return (
        <main className="page">
            <h1>Página não encontrada</h1>
            <p>O endereço que você abriu não existe.</p>
            <p className="back-link-row">
                <Link to="/">← Voltar para as partidas</Link>
            </p>
        </main>
    );
}

export default NotFoundPage;
