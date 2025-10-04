import pandas as pd
import sqlalchemy
from sqlalchemy import inspect
from openai import OpenAI
import json
import logging
import os

logger = logging.getLogger(__name__)


class SchemaAnalyzer:
    def __init__(self, connection_string: str):
        self.engine = sqlalchemy.create_engine(connection_string)
        self.client = OpenAI(api_key=os.getenv("OPENAI_API_KEY"))
    
    def analyze_database(self, company_name: str) -> dict:
        """
        Analyze entire database schema and generate semantic understanding
        """
        try:
            # Get all tables
            inspector = inspect(self.engine)
            tables = inspector.get_table_names()
            
            if not tables:
                return {
                    "error": "No tables found in database",
                    "semantic_schema": None
                }
            
            logger.info(f"Found {len(tables)} tables: {tables}")
            
            # Analyze each table
            schema_info = {}
            
            for table in tables:
                try:
                    table_analysis = self._analyze_table(table)
                    schema_info[table] = table_analysis
                except Exception as e:
                    logger.error(f"Error analyzing table {table}: {str(e)}")
                    schema_info[table] = {"error": str(e)}
            
            # Generate semantic schema using LLM
            semantic_schema = self._generate_semantic_schema(schema_info, company_name)
            
            # Identify unclear fields
            unclear_fields = self._identify_unclear_fields(schema_info, semantic_schema)
            
            return {
                "semantic_schema": semantic_schema,
                "unclear_fields": unclear_fields,
                "raw_schema": schema_info
            }
            
        except Exception as e:
            logger.error(f"Error in analyze_database: {str(e)}")
            raise
    
    def _analyze_table(self, table_name: str) -> dict:
        """
        Analyze a single table using pandas
        """
        # Read table (limit to first 1000 rows for analysis)
        query = f"SELECT * FROM {table_name} LIMIT 1000"
        df = pd.read_sql(query, self.engine)
        
        analysis = {
            "table_name": table_name,
            "row_count": len(df),
            "columns": []
        }
        
        # Analyze each column
        for col in df.columns:
            col_info = {
                "name": col,
                "dtype": str(df[col].dtype),
                "null_count": int(df[col].isnull().sum()),
                "null_percentage": float(df[col].isnull().mean() * 100),
                "unique_count": int(df[col].nunique()),
                "is_unique": bool(df[col].nunique() == len(df)),
                "sample_values": df[col].dropna().head(5).tolist()
            }
            
            # Numeric columns
            if pd.api.types.is_numeric_dtype(df[col]):
                col_info["min"] = float(df[col].min()) if not df[col].isna().all() else None
                col_info["max"] = float(df[col].max()) if not df[col].isna().all() else None
                col_info["mean"] = float(df[col].mean()) if not df[col].isna().all() else None
                col_info["std"] = float(df[col].std()) if not df[col].isna().all() else None
            
            # Datetime columns
            if pd.api.types.is_datetime64_any_dtype(df[col]):
                col_info["is_timestamp"] = True
                if not df[col].isna().all():
                    col_info["min_date"] = str(df[col].min())
                    col_info["max_date"] = str(df[col].max())
            
            analysis["columns"].append(col_info)
        
        return analysis
    
    def _generate_semantic_schema(self, schema_info: dict, company_name: str) -> dict:
        """
        Use LLM to generate semantic understanding of the schema
        """
        try:
            # Prepare schema summary for LLM
            schema_summary = self._prepare_schema_summary(schema_info)
            
            prompt = f"""
Sen bir veritabanı analiz uzmanısın. Aşağıdaki şirkete ait veritabanı şemasını analiz et:

Şirket Adı: {company_name}

Veritabanı Şeması:
{json.dumps(schema_summary, indent=2, ensure_ascii=False)}

Görevlerin:
1. Bu hangi sektöre ait olabilir? (restaurant, e-commerce, manufacturing, retail, service, vb.)
2. Her tablo ne anlama geliyor? (Türkçe semantic isim ve açıklama)
3. Hangi kolonlar "target" olabilir? (tahmin yapılacak metrikler: satış tutarı, miktar, vb.)
4. Hangi kolonlar "metric"? (KPI hesaplamaları için: price, quantity, amount, vb.)
5. Hangi kolonlar "dimension"? (gruplandırma için: category, status, type, vb.)
6. Hangi kolon timestamp? (zaman serisi analizi için)
7. Tablolar arası ilişkileri tahmin et
8. Dashboard için hangi KPI'ları gösterebiliriz?

JSON formatında döndür (Türkçe açıklamalar):
{{
  "industry": "...",
  "industry_tr": "...",
  "confidence": 0.0-1.0,
  "tables": {{
    "table_name": {{
      "semantic_name": "Türkçe isim",
      "description": "Ne için kullanılıyor",
      "target_columns": ["kolon1", "kolon2"],
      "metric_columns": ["kolon3", "kolon4"],
      "dimension_columns": ["kolon5"],
      "timestamp_column": "kolon6",
      "primary_key": "id"
    }}
  }},
  "relationships": [
    {{"from": "table1.column", "to": "table2.column", "type": "one-to-many"}}
  ],
  "suggested_kpis": [
    {{"name": "Günlük Satışlar", "calculation": "SUM(amount)", "table": "orders"}},
    {{"name": "Ortalama Sipariş", "calculation": "AVG(amount)", "table": "orders"}}
  ],
  "dashboard_recommendations": [
    "Gerçek zamanlı satış grafiği",
    "En çok satan ürünler"
  ]
}}
"""
            
            response = self.client.chat.completions.create(
                model="gpt-4o-mini",
                messages=[
                    {"role": "system", "content": "Sen bir veritabanı analiz uzmanısın. Türkçe ve JSON formatında yanıt veriyorsun."},
                    {"role": "user", "content": prompt}
                ],
                response_format={"type": "json_object"},
                temperature=0.3
            )
            
            semantic_schema = json.loads(response.choices[0].message.content)
            return semantic_schema
            
        except Exception as e:
            logger.error(f"Error generating semantic schema: {str(e)}")
            # Return basic fallback schema
            return {
                "industry": "unknown",
                "industry_tr": "Bilinmeyen",
                "confidence": 0.0,
                "tables": {},
                "error": str(e)
            }
    
    def _prepare_schema_summary(self, schema_info: dict) -> dict:
        """
        Prepare a concise summary for LLM (to avoid token limits)
        """
        summary = {}
        
        for table_name, table_data in schema_info.items():
            if "error" in table_data:
                continue
            
            summary[table_name] = {
                "row_count": table_data.get("row_count", 0),
                "columns": [
                    {
                        "name": col["name"],
                        "type": col["dtype"],
                        "unique_count": col["unique_count"],
                        "null_pct": round(col["null_percentage"], 2),
                        "sample": col["sample_values"][:3]  # Only first 3 samples
                    }
                    for col in table_data.get("columns", [])
                ]
            }
        
        return summary
    
    def _identify_unclear_fields(self, schema_info: dict, semantic_schema: dict) -> list:
        """
        Identify fields that need clarification from admin
        """
        unclear = []
        
        # Check confidence
        if semantic_schema.get("confidence", 1.0) < 0.7:
            unclear.append({
                "type": "industry",
                "question": f"Bu şirket '{semantic_schema.get('industry_tr', 'bilinmeyen')}' sektöründe mi?",
                "suggested": semantic_schema.get("industry_tr")
            })
        
        # Check for ambiguous column names
        ambiguous_patterns = ["col", "field", "data", "value", "temp", "test"]
        
        for table_name, table_data in schema_info.items():
            if "error" in table_data:
                continue
            
            for col in table_data.get("columns", []):
                col_name = col["name"].lower()
                
                # Check if column name is too generic
                if any(pattern in col_name for pattern in ambiguous_patterns):
                    unclear.append({
                        "type": "column",
                        "table": table_name,
                        "column": col["name"],
                        "question": f"'{table_name}.{col['name']}' kolonu ne anlama geliyor?",
                        "sample_values": col["sample_values"][:3]
                    })
        
        return unclear[:10]  # Limit to 10 unclear fields
