import React from 'react';
import { IconX } from '@tabler/icons-react';
import {
  Chart as ChartJS,
  CategoryScale,
  LinearScale,
  BarElement,
  LineElement,
  ArcElement,
  PointElement,
  RadialLinearScale,
  Title,
  Tooltip,
  Legend,
  Filler
} from 'chart.js';
import { Bar, Line, Pie, Doughnut, Radar } from 'react-chartjs-2';
import '../styles/ReportModal.css';

// Chart.js kayıt
ChartJS.register(
  CategoryScale,
  LinearScale,
  BarElement,
  LineElement,
  ArcElement,
  PointElement,
  RadialLinearScale,
  Title,
  Tooltip,
  Legend,
  Filler
);

const ReportModal = ({ isOpen, onClose, reportData }) => {
  if (!isOpen || !reportData) return null;

  const renderChart = (chart, index) => {
    const chartData = {
      labels: chart.data.labels || [],
      datasets: [{
        label: chart.title,
        data: chart.data.values || [],
        backgroundColor: [
          'rgba(102, 126, 234, 0.8)',
          'rgba(118, 75, 162, 0.8)',
          'rgba(16, 185, 129, 0.8)',
          'rgba(245, 158, 11, 0.8)',
          'rgba(239, 68, 68, 0.8)',
          'rgba(59, 130, 246, 0.8)',
        ],
        borderColor: [
          'rgba(102, 126, 234, 1)',
          'rgba(118, 75, 162, 1)',
          'rgba(16, 185, 129, 1)',
          'rgba(245, 158, 11, 1)',
          'rgba(239, 68, 68, 1)',
          'rgba(59, 130, 246, 1)',
        ],
        borderWidth: 2,
        fill: chart.type === 'area'
      }]
    };

    const options = {
      responsive: true,
      maintainAspectRatio: true,
      plugins: {
        legend: {
          position: 'top',
        },
        title: {
          display: true,
          text: chart.title,
          font: {
            size: 16,
            weight: 'bold'
          }
        }
      }
    };

    switch (chart.type) {
      case 'bar':
        return <Bar key={index} data={chartData} options={options} />;
      case 'line':
      case 'area':
        return <Line key={index} data={chartData} options={options} />;
      case 'pie':
        return <Pie key={index} data={chartData} options={options} />;
      case 'donut':
        return <Doughnut key={index} data={chartData} options={options} />;
      case 'radar':
        return <Radar key={index} data={chartData} options={options} />;
      default:
        return <Bar key={index} data={chartData} options={options} />;
    }
  };

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal-content" onClick={(e) => e.stopPropagation()}>
        <div className="modal-header">
          <div>
            <h2>{reportData.title}</h2>
            <p className="modal-description">{reportData.description}</p>
          </div>
          <button className="modal-close-btn" onClick={onClose}>
            <IconX size={24} />
          </button>
        </div>

        <div className="modal-body">
          {/* Grafikler */}
          <div className="charts-container">
            {reportData.charts && reportData.charts.map((chart, index) => (
              <div key={index} className="chart-item">
                {renderChart(chart, index)}
              </div>
            ))}
          </div>

          {/* Insights */}
          {reportData.insights && reportData.insights.length > 0 && (
            <div className="insights-container">
              <h3>💡 Önemli Notlar</h3>
              <div className="insights-list">
                {reportData.insights.map((insight, index) => (
                  <div key={index} className={`insight-card ${insight.type}`}>
                    <strong>{insight.title}</strong>
                    <p>{insight.description}</p>
                  </div>
                ))}
              </div>
            </div>
          )}

          {/* AI Cevabı (eğer soru cevapsa) */}
          {reportData.answer && (
            <div className="answer-container">
              <h3>🤖 AI Cevabı</h3>
              <div className="answer-box">
                <p><strong>Soru:</strong> {reportData.question}</p>
                <p><strong>Cevap:</strong> {reportData.answer}</p>
              </div>
            </div>
          )}
        </div>

        <div className="modal-footer">
          <button className="btn-secondary" onClick={onClose}>
            Kapat
          </button>
          <button className="btn-primary" onClick={() => {
            alert('📥 İndirme özelliği yakında eklenecek!');
          }}>
            📥 PDF İndir
          </button>
        </div>
      </div>
    </div>
  );
};

export default ReportModal;
