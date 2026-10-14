import { Link } from 'react-router';
import StatePanel from './ui/StatePanel';

function NotFoundPage() {
    return (
        <main className="page container">
            <StatePanel
                code="404"
                title="Página não encontrada"
                titleAs="h1"
                actions={<Link className="button-secondary" to="/">Voltar para as partidas</Link>}
            >
                <p>O endereço que você abriu não existe.</p>
            </StatePanel>
        </main>
    );
}

export default NotFoundPage;
