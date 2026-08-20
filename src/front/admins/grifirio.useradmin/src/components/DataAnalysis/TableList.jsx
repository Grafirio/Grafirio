import React, { useState, useEffect } from 'react';
import { getTables } from '../../services/dataAnalysisService';
import './TableList.css';

// Bileşen artık ham bağlantı bilgisi değil bağlantı KİMLİĞİ alıyor: tablo
// listesi ucu kimlikle çalışıyor, böylece bridge üzerinden okunan bağlantılar
// da listelenebiliyor ve veritabanı parolasının tarayıcıya inmesi gerekmiyor.
const TableList = ({ connectionId, onTableSelect, multiSelect = false, selectedTables = [] }) => {
  const [tables, setTables] = useState([]);
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState('');
  const [searchTerm, setSearchTerm] = useState('');
  const [selectedTable, setSelectedTable] = useState(null);
  const [checkedTables, setCheckedTables] = useState(selectedTables);

  useEffect(() => {
    if (connectionId) {
      loadTables();
    }
  }, [connectionId]);

  const loadTables = async () => {
    setIsLoading(true);
    setError('');

    try {
      const result = await getTables(connectionId);

      if (result.success) {
        setTables(result.tables);
      } else {
        setError(result.message || 'Tablolar yüklenemedi');
      }
    } catch (err) {
      setError('Tablolar yüklenirken hata oluştu: ' + err.message);
    } finally {
      setIsLoading(false);
    }
  };

  const handleTableClick = (table) => {
    if (multiSelect) {
      // Multi-select mode: toggle checkbox
      handleCheckboxChange(table);
    } else {
      // Single select mode: show details
      setSelectedTable(table.fullName);
      if (onTableSelect) {
        onTableSelect(table);
      }
    }
  };

  const handleCheckboxChange = (table) => {
    const isChecked = checkedTables.some(t => t.fullName === table.fullName);
    let newChecked;
    
    if (isChecked) {
      newChecked = checkedTables.filter(t => t.fullName !== table.fullName);
    } else {
      newChecked = [...checkedTables, table];
    }
    
    setCheckedTables(newChecked);
    
    if (onTableSelect) {
      onTableSelect(newChecked);
    }
  };

  const isTableChecked = (table) => {
    return checkedTables.some(t => t.fullName === table.fullName);
  };

  const filteredTables = tables.filter(table =>
    table.tableName.toLowerCase().includes(searchTerm.toLowerCase()) ||
    table.schema.toLowerCase().includes(searchTerm.toLowerCase())
  );

  if (isLoading) {
    return (
      <div className="table-list-container">
        <div className="loading-state">
          <div className="spinner-large"></div>
          <p>Tablolar yükleniyor...</p>
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="table-list-container">
        <div className="error-state">
          <i className="ti ti-alert-circle"></i>
          <p>{error}</p>
          <button className="btn btn-primary" onClick={loadTables}>
            <i className="ti ti-refresh"></i> Tekrar Dene
          </button>
        </div>
      </div>
    );
  }

  return (
    <div className="table-list-container">
      <div className="table-list-header">
        <div className="header-info">
          <h3>📋 Veritabanı Tabloları</h3>
          <span className="table-count">{tables.length} tablo bulundu</span>
        </div>
        
        <div className="search-box">
          <i className="ti ti-search"></i>
          <input
            type="text"
            placeholder="Tablo ara..."
            value={searchTerm}
            onChange={(e) => setSearchTerm(e.target.value)}
          />
        </div>

        <button className="btn btn-refresh" onClick={loadTables}>
          <i className="ti ti-refresh"></i>
        </button>
      </div>

      {filteredTables.length === 0 ? (
        <div className="empty-state">
          <i className="ti ti-database-off"></i>
          <p>Tablo bulunamadı</p>
        </div>
      ) : (
        <div className="tables-grid">
          {filteredTables.map((table, index) => (
            <div
              key={index}
              className={`table-card ${selectedTable === table.fullName ? 'selected' : ''} ${isTableChecked(table) ? 'checked' : ''}`}
              onClick={() => handleTableClick(table)}
            >
              {multiSelect && (
                <div className="table-checkbox" onClick={(e) => e.stopPropagation()}>
                  <input
                    type="checkbox"
                    checked={isTableChecked(table)}
                    onChange={() => handleCheckboxChange(table)}
                  />
                </div>
              )}
              <div className="table-icon">
                <i className="ti ti-table"></i>
              </div>
              <div className="table-info">
                <h4>{table.tableName}</h4>
                <span className="schema-badge">{table.schema}</span>
              </div>
              {!multiSelect && (
                <div className="table-arrow">
                  <i className="ti ti-chevron-right"></i>
                </div>
              )}
            </div>
          ))}
        </div>
      )}
    </div>
  );
};

export default TableList;
