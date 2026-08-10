import pandas as pd
from typing import Dict, Any
import os
import json
import logging
from datetime import datetime

from data_port import DataPort

logger = logging.getLogger(__name__)


def _quote(qualified: str) -> str:
    """`sema.tablo` -> `[sema].[tablo]`. Ad tanimlayici oldugu icin
    parametrelenemiyor; en azindan koseli parantez kacisi yapiliyor."""
    parts = qualified.replace("[", "").replace("]", "").split(".")
    return ".".join(f"[{part}]" for part in parts if part)


class AutoTrainer:
    def __init__(self, data: DataPort, semantic_schema: Dict, company_id: str):
        # Baglanti degil, sorgu calistiran bir kapi — bkz. data_port.py.
        self.data = data
        self.semantic_schema = semantic_schema
        self.company_id = company_id
        self.models_dir = os.path.join(
            os.getenv("MODEL_STORAGE_PATH", "/app/models"),
            company_id
        )
        os.makedirs(self.models_dir, exist_ok=True)
    
    async def train_all_tables(self) -> Dict[str, Any]:
        """
        Train models for all tables in the semantic schema
        """
        results = {
            "company_id": self.company_id,
            "trained_at": datetime.utcnow().isoformat(),
            "tables": {}
        }
        
        tables_config = self.semantic_schema.get("tables", {})
        
        for table_name, table_config in tables_config.items():
            try:
                logger.info(f"Training models for table: {table_name}")
                
                # Load data. `read_sql_table` bir baglanti nesnesi istiyordu;
                # kapi tablo degil sorgu konustugu icin SELECT acik yaziliyor.
                df = self.data.read_sql(f"SELECT * FROM {_quote(table_name)}")
                
                if df.empty:
                    logger.warning(f"Table {table_name} is empty, skipping")
                    continue
                
                # Train models for this table
                table_results = await self._train_table_models(table_name, df, table_config)
                results["tables"][table_name] = table_results
                
            except Exception as e:
                logger.error(f"Error training models for {table_name}: {str(e)}")
                results["tables"][table_name] = {"error": str(e)}
        
        # Save metadata
        self._save_metadata(results)
        
        return results
    
    async def _train_table_models(self, table_name: str, df: pd.DataFrame, table_config: Dict) -> Dict:
        """
        Train regression and anomaly detection models for a table
        """
        results = {
            "table": table_name,
            "models": []
        }
        
        # Get target columns for regression
        target_columns = table_config.get("target_columns", [])
        
        # Train regression models
        for target in target_columns:
            if target not in df.columns:
                continue
            
            # Check if target is numeric
            if not pd.api.types.is_numeric_dtype(df[target]):
                continue
            
            try:
                model_result = await self._train_regression(table_name, df, target)
                results["models"].append(model_result)
            except Exception as e:
                logger.error(f"Error training regression for {table_name}.{target}: {str(e)}")
        
        # Train anomaly detection
        try:
            anomaly_result = await self._train_anomaly_detection(table_name, df)
            results["models"].append(anomaly_result)
        except Exception as e:
            logger.error(f"Error training anomaly detection for {table_name}: {str(e)}")
        
        return results
    
    async def _train_regression(self, table_name: str, df: pd.DataFrame, target: str) -> Dict:
        """
        Train regression model using PyCaret
        """
        try:
            from pycaret.regression import setup, compare_models, save_model
            
            logger.info(f"Training regression: {table_name}.{target}")
            
            # Prepare data - remove non-numeric columns except target
            numeric_cols = df.select_dtypes(include=['number']).columns.tolist()
            
            if target not in numeric_cols:
                raise Exception(f"Target {target} is not numeric")
            
            # Keep only numeric columns
            df_numeric = df[numeric_cols].copy()
            
            # Remove rows with missing target
            df_numeric = df_numeric.dropna(subset=[target])
            
            if len(df_numeric) < 100:
                raise Exception(f"Not enough data (< 100 rows) for {target}")
            
            # PyCaret 3.x setup ('silent' parametresi 2.x'te vardi, 3.x'te TypeError verir)
            exp = setup(
                data=df_numeric,
                target=target,
                session_id=123,
                verbose=False,
                html=False,
                n_jobs=1
            )
            
            # Compare models and select best
            best_model = compare_models(
                n_select=1,
                sort='MAE',
                verbose=False
            )
            
            # Save model
            model_name = f"{table_name}_{target}_regression"
            model_path = os.path.join(self.models_dir, model_name)
            save_model(best_model, model_path)
            
            logger.info(f"Regression model saved: {model_name}")
            
            return {
                "type": "regression",
                "table": table_name,
                "target": target,
                "model_name": type(best_model).__name__,
                "model_path": f"{model_name}.pkl",
                "trained_at": datetime.utcnow().isoformat()
            }
            
        except Exception as e:
            logger.error(f"Regression training failed: {str(e)}")
            raise
    
    async def _train_anomaly_detection(self, table_name: str, df: pd.DataFrame) -> Dict:
        """
        Train anomaly detection model using PyCaret
        """
        try:
            from pycaret.anomaly import setup, create_model, save_model
            
            logger.info(f"Training anomaly detection: {table_name}")
            
            # Keep only numeric columns
            numeric_cols = df.select_dtypes(include=['number']).columns.tolist()
            df_numeric = df[numeric_cols].copy()
            
            # Remove missing values
            df_numeric = df_numeric.dropna()
            
            if len(df_numeric) < 100:
                raise Exception(f"Not enough data (< 100 rows) for anomaly detection")
            
            # PyCaret 3.x setup ('silent' parametresi 2.x'te vardi, 3.x'te TypeError verir)
            exp = setup(
                data=df_numeric,
                session_id=123,
                verbose=False,
                html=False
            )
            
            # Create isolation forest model
            model = create_model('iforest')
            
            # Save model
            model_name = f"{table_name}_anomaly"
            model_path = os.path.join(self.models_dir, model_name)
            save_model(model, model_path)
            
            logger.info(f"Anomaly model saved: {model_name}")
            
            return {
                "type": "anomaly",
                "table": table_name,
                "model_name": "IsolationForest",
                "model_path": f"{model_name}.pkl",
                "trained_at": datetime.utcnow().isoformat()
            }
            
        except Exception as e:
            logger.error(f"Anomaly detection training failed: {str(e)}")
            raise
    
    def _save_metadata(self, results: Dict):
        """
        Save training metadata
        """
        metadata_path = os.path.join(self.models_dir, "metadata.json")
        
        with open(metadata_path, 'w') as f:
            json.dump(results, f, indent=2)
        
        logger.info(f"Metadata saved: {metadata_path}")
