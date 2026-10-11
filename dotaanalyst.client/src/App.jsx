import { BrowserRouter, Route, Routes } from 'react-router';
import MatchAnalysisPage from './analysis/MatchAnalysisPage';
import MatchesPage from './matches/MatchesPage';
import NotFoundPage from './NotFoundPage';
import './App.css';

function App() {
    return (
        <BrowserRouter>
            <Routes>
                <Route path="/" element={<MatchesPage />} />
                <Route path="/matches/:matchId" element={<MatchAnalysisPage />} />
                <Route path="*" element={<NotFoundPage />} />
            </Routes>
        </BrowserRouter>
    );
}

export default App;
