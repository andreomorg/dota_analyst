import { BrowserRouter, Route, Routes } from 'react-router';
import MatchAnalysisPage from './analysis/MatchAnalysisPage';
import CurrentUserProvider from './auth/CurrentUserProvider';
import LoginFailedNotice from './layout/LoginFailedNotice';
import SiteHeader from './layout/SiteHeader';
import MatchesPage from './matches/MatchesPage';
import NotFoundPage from './NotFoundPage';
import './App.css';
import './StitchDesign.css';

function App() {
    return (
        <BrowserRouter>
            {/* Every page below can ask who is logged in with useCurrentUser(). */}
            <CurrentUserProvider>
                <SiteHeader />
                <LoginFailedNotice />
                <Routes>
                    <Route path="/" element={<MatchesPage />} />
                    <Route path="/matches/:matchId" element={<MatchAnalysisPage />} />
                    <Route path="*" element={<NotFoundPage />} />
                </Routes>
            </CurrentUserProvider>
        </BrowserRouter>
    );
}

export default App;
