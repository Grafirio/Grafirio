import React, { useState, useEffect } from "react";
import { useNavigate } from "react-router-dom";
import { IconLoader2, IconAlertCircle, IconDatabase, IconPlus, IconChevronRight } from "@tabler/icons-react";
import "../styles/DashboardPage.css";

const DashboardPage = () => {
  const navigate = useNavigate();
  const [completedAnalyses, setCompletedAnalyses] = useState([]);
  const [activeAnalyses, setActiveAnalyses] = useState([]);

  useEffect(() => {
    loadAnalyses();
    const interval = setInterval(loadAnalyses, 8000);
    return () => clearInterval(interval);
  }, []);

  const loadAnalyses = () => {
    const stored = localStorage.getItem("activeAnalyses");
    if (!stored) return;
    const all = JSON.parse(stored);
    setActiveAnalyses(all.filter(a => a.status !== "completed" && a.status !== "failed"));
    setCompletedAnalyses(all.filter(a => a.status === "completed" || a.status === "failed"));
  };

  const handleCardDoubleClick = (analysis) => {
    navigate(`/canvas/${analysis.requestId}`);
  };

  const formatDate = (str) =>
    str ? new Date(str).toLocaleDateString("tr-TR", { day: "2-digit", month: "short", year: "numeric" }) : "\u2014";

  return (
    <div className="db-home">
      <div className="db-home-header">
        <div className="db-home-title">
          <IconDatabase size={28} strokeWidth={1.5} />
          <div>
            <h1>Veritaban\u0131 Ba\u011flant\u0131lar\u0131</h1>
            <p>Analiz tuvaline a\u00e7mak i\u00e7in bir veritaban\u0131na <strong>\u00e7ift t\u0131klay\u0131n</strong></p>
          </div>
        </div>
        <button className="db-btn-new" onClick={() => navigate("/data-analysis")}>
          <IconPlus size={16} />
          Yeni Analiz
        </button>
      </div>

      {activeAnalyses.length > 0 && (
        <div className="db-processing-bar">
          {activeAnalyses.map(a => (
            <div key={a.requestId} className="db-processing-item">
              <IconLoader2 size={14} className="spin" />
              <span>{a.database}</span>
              <span className="db-processing-pct">{a.progress ?? 0}%</span>
            </div>
          ))}
        </div>
      )}

      {completedAnalyses.length === 0 ? (
        <div className="db-empty">
          <IconAlertCircle size={56} strokeWidth={1.2} />
          <h2>Hen\u00fcz analiz yok</h2>
          <p>Veri Analizi sayfas\u0131nda yeni bir veritaban\u0131 ba\u011flant\u0131s\u0131 olu\u015fturun.</p>
          <button className="db-btn-new" onClick={() => navigate("/data-analysis")}>
            <IconPlus size={16} /> Yeni Analiz Ba\u015flat
          </button>
        </div>
      ) : (
        <div className="db-grid">
          {completedAnalyses.map(analysis => (
            <div
              key={analysis.requestId}
              className={`db-card ${analysis.status === "failed" ? "db-card--failed" : ""}`}
              onDoubleClick={() => handleCardDoubleClick(analysis)}
              title="Analiz tuvaline a\u00e7mak i\u00e7in \u00e7ift t\u0131klay\u0131n"
            >
              <div className="db-card-icon-wrap">
                <IconDatabase size={32} strokeWidth={1.2} />
              </div>
              <div className="db-card-body">
                <div className="db-card-name">{analysis.database}</div>
                <div className="db-card-meta">
                  <span className="db-card-tables">{analysis.tables?.length ?? 0} tablo</span>
                  <span className={`db-card-badge ${analysis.status === "failed" ? "badge--failed" : "badge--ok"}`}>
                    {analysis.status === "failed" ? "Ba\u015far\u0131s\u0131z" : "Haz\u0131r"}
                  </span>
                </div>
                <div className="db-card-date">{formatDate(analysis.completedAt || analysis.updatedAt)}</div>
              </div>
              <div className="db-card-cta">
                <IconChevronRight size={18} />
                <span>Tuvali A\u00e7</span>
              </div>
            </div>
          ))}
          <div className="db-card db-card--add" onClick={() => navigate("/data-analysis")}>
            <div className="db-card-add-inner">
              <IconPlus size={28} strokeWidth={1.5} />
              <span>Yeni Ba\u011flant\u0131</span>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};

export default DashboardPage;
