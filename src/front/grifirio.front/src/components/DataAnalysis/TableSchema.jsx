import React, { useState, useEffect } from 'react';
import { getTableSchema } from '../../services/dataAnalysisService';
import './TableSchema.css';

const TableSchema = ({ table, connectionInfo, onSendToAI }) => {
  const [schema, setSchema] = useState(null);
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState('');

  useEffect(() => {
    if (table && connectionInfo) {
      loadSchema();
    }
  }, [table]);

  const loadSchema = async () => {
    setIsLoading(true);
    setError('');

    try {
      const result = await getTableSchema(table.fullName, connectionInfo);
      setSchema(result);
    } catch (err) {
      setError('Şema yüklenemedi: ' + err.message);
    } finally {
      setIsLoading(false);
    }
  };

  const getDataTypeColor = (dataType) => {
    const type = dataType.toLowerCase();
    if (type.includes('int') || type.includes('decimal') || type.includes('numeric')) return 'type-number';
    if (type.includes('char') || type.includes('text')) return 'type-string';
    if (type.includes('date') || type.includes('time')) return 'type-date';
    if (type.includes('bit') || type.includes('bool')) return 'type-boolean';
    return 'type-other';
  };

  const handleSendToAI = () => {
    if (onSendToAI && schema) {
      onSendToAI({
        table: table.tableName,
        schema: table.schema,
        columns: schema.columns,
        rowCount: schema.rowCount,
        connectionInfo: connectionInfo
      });
    }
  };

  if (isLoading) {
    return (
      <div className="table-schema-container">
        <div className="loading-state">
          <div className="spinner-large"></div>
          <p>Tablo şeması yükleniyor...</p>
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="table-schema-container">
        <div className="error-state">
          <i className="ti ti-alert-circle"></i>
          <p>{error}</p>
          <button className="btn btn-primary" onClick={loadSchema}>
            <i className="ti ti-refresh"></i> Tekrar Dene
          </button>
        </div>
      </div>
    );
  }

  if (!schema) return null;

  return (
    <div className="table-schema-container">
      <div className="schema-header">
        <div className="schema-title">
          <i className="ti ti-database"></i>
          <div>
            <h3>{schema.schema}.{schema.tableName}</h3>
            <span className="row-count">
              <i className="ti ti-file-analytics"></i>
              {schema.rowCount.toLocaleString()} satır
            </span>
          </div>
        </div>
        
        <button className="btn btn-ai" onClick={handleSendToAI}>
          <i className="ti ti-brain"></i>
          AI Analizi Başlat
        </button>
      </div>

      <div className="columns-table-wrapper">
        <table className="columns-table">
          <thead>
            <tr>
              <th>#</th>
              <th>Kolon Adı</th>
              <th>Veri Tipi</th>
              <th>Nullable</th>
              <th>Max Length</th>
            </tr>
          </thead>
          <tbody>
            {schema.columns.map((column, index) => (
              <tr key={index}>
                <td className="column-index">{index + 1}</td>
                <td className="column-name">
                  <i className="ti ti-column"></i>
                  {column.columnName}
                </td>
                <td>
                  <span className={`data-type ${getDataTypeColor(column.dataType)}`}>
                    {column.dataType}
                  </span>
                </td>
                <td className="nullable-cell">
                  {column.isNullable ? (
                    <span className="badge badge-yes">✓ Yes</span>
                  ) : (
                    <span className="badge badge-no">✗ No</span>
                  )}
                </td>
                <td className="max-length">
                  {column.maxLength || '-'}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <div className="schema-footer">
        <div className="schema-stats">
          <div className="stat-item">
            <i className="ti ti-columns"></i>
            <span>{schema.columns.length} Kolon</span>
          </div>
          <div className="stat-item">
            <i className="ti ti-shield-check"></i>
            <span>{schema.columns.filter(c => !c.isNullable).length} NOT NULL</span>
          </div>
          <div className="stat-item">
            <i className="ti ti-question-mark"></i>
            <span>{schema.columns.filter(c => c.isNullable).length} Nullable</span>
          </div>
        </div>
      </div>
    </div>
  );
};

export default TableSchema;
