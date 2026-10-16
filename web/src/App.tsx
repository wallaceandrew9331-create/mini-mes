import { NavLink, Navigate, Route, Routes } from "react-router-dom";
import BoardPage from "./pages/BoardPage";
import GenealogyPage from "./pages/GenealogyPage";
import RecallPage from "./pages/RecallPage";
import TravelerPage from "./pages/TravelerPage";

export default function App() {
  return (
    <div className="shell">
      <header className="topbar">
        <span className="brand">Mini MES</span>
        <nav>
          <NavLink to="/" end>
            Work orders
          </NavLink>
          <NavLink to="/genealogy">Genealogy</NavLink>
          <NavLink to="/recall">Recall</NavLink>
        </nav>
        <a className="api-link" href="/swagger" target="_blank" rel="noreferrer">
          API
        </a>
      </header>
      <main>
        <Routes>
          <Route path="/" element={<BoardPage />} />
          <Route path="/work-orders/:id" element={<TravelerPage />} />
          <Route path="/genealogy" element={<GenealogyPage />} />
          <Route path="/genealogy/:serial" element={<GenealogyPage />} />
          <Route path="/recall" element={<RecallPage />} />
          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </main>
    </div>
  );
}
