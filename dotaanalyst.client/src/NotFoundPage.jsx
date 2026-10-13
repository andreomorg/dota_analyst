import { Link } from 'react-router';

function NotFoundPage() {
    return (
        <main className="page container">
            <div className="empty-state">
                <h1>Página não encontrada</h1>
                <p>O endereço que você abriu não existe.</p>
                <Link className="button-primary" to="/">Voltar para as partidas</Link>
            </div>
        </main>
    );
}

export default NotFoundPage;
