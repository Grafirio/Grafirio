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
    str ? new Date(str).toLocaleDateString("tr-TR", { day: "2-digit", month: "short", year: "numeric" }) : "—";

  return (
    <div className="db-home">
      <div className="db-home-header">
        <div className="db-home-title">
          <IconDatabase size={28} strokeWidth={1.5} />
          <div>
            <h1>Veritabanı Bağlantıları</h1>
            <p>Analiz tuvaline açmak için bir veritabanına <strong>çift tıklayın</strong></p>
          </div>
        </div>
        <button className="db-btn-new" onClick={() => navigate("/settings/sql-connection")}>
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
          <h2>Henüz analiz yok</h2>
          <p>Ayarlar &gt; SQL Bağlantı sayfasında bağlantı ekleyin ve Analiz Et'e tıklayın.</p>
          <button className="db-btn-new" onClick={() => navigate("/settings/sql-connection")}>
            <IconPlus size={16} /> Yeni Analiz Başlat
          </button>
        </div>
      ) : (
        <div className="db-grid">
          {completedAnalyses.map(analysis => (
            <div
              key={analysis.requestId}
              className={`db-card ${analysis.status === "failed" ? "db-card--failed" : ""}`}
              onDoubleClick={() => handleCardDoubleClick(analysis)}
              title="Analiz tuvaline açmak için çift tıklayın"
            >
              <div className="db-card-icon-wrap">
                <IconDatabase size={32} strokeWidth={1.2} />
              </div>
              <div className="db-card-body">
                <div className="db-card-name">{analysis.database}</div>
                <div className="db-card-meta">
                  <span className="db-card-tables">{analysis.tables?.length ?? 0} tablo</span>
                  <span className={`db-card-badge ${analysis.status === "failed" ? "badge--failed" : "badge--ok"}`}>
                    {analysis.status === "failed" ? "Başarısız" : "Hazır"}
                  </span>
                </div>
                <div className="db-card-date">{formatDate(analysis.completedAt || analysis.updatedAt)}</div>
              </div>
              <div className="db-card-cta">
                <IconChevronRight size={18} />
                <span>Tuvali Aç</span>
              </div>
            </div>
          ))}
          <div className="db-card db-card--add" onClick={() => navigate("/settings/sql-connection")}>
            <div className="db-card-add-inner">
              <IconPlus size={28} strokeWidth={1.5} />
              <span>Yeni Bağlantı</span>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};

export default DashboardPage;
