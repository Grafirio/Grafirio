import pandas as pd
import os
import json
import logging
from typing import Dict, Any
from pycaret.regression import load_model as load_reg_model, predict_model as predict_reg
from pycaret.anomaly import load_model as load_anom_model, predict_model as predict_anom

logger = logging.getLogger(__name__)


class RealtimePredictor:
    def __init__(self):
        self.models = {}
        self.models_dir = os.getenv("MODEL_STORAGE_PATH", "/app/models")
    
    def models_exist(self, company_id: str, table_name: str) -> bool:
        """
        Check if models exist for this company/table
        """
        company_dir = os.path.join(self.models_dir, company_id)
        
        if not os.path.exists(company_dir):
            return False
        
        # Check metadata
        metadata_path = os.path.join(company_dir, "metadata.json")
        
        if not os.path.exists(metadata_path):
            return False
        
        with open(metadata_path, 'r') as f:
            metadata = json.load(f)
        
        tables = metadata.get("tables", {})
        return table_name in tables
    
    def load_models(self, company_id: str, table_name: str):
        """
        Load trained models for a company/table
        """
        company_dir = os.path.join(self.models_dir, company_id)
        metadata_path = os.path.join(company_dir, "metadata.json")
        
        with open(metadata_path, 'r') as f:
            metadata = json.load(f)
        
        table_models = metadata["tables"][table_name]["models"]
        
        self.models[table_name] = {}
        
        for model_info in table_models:
            model_type = model_info["type"]
            model_path = os.path.join(company_dir, model_info["model_path"])
            
            try:
                if model_type == "regression":
                    model = load_reg_model(model_path.replace('.pkl', ''))
                    target = model_info["target"]
                    
                    if "regression" not in self.models[table_name]:
                        self.models[table_name]["regression"] = {}
                    
                    self.models[table_name]["regression"][target] = model
                    
                elif model_type == "anomaly":
                    model = load_anom_model(model_path.replace('.pkl', ''))
                    self.models[table_name]["anomaly"] = model
                
                logger.info(f"Loaded {model_type} model for {table_name}")
                
            except Exception as e:
                logger.error(f"Error loading model {model_path}: {str(e)}")
    
    def predict(self, data: Dict[str, Any]) -> Dict[str, Any]:
        """
        Make predictions on new data
        """
        results = {}
        
        # Convert to DataFrame
        df = pd.DataFrame([data])
        
        # Get table name (assume first loaded table for now)
        table_name = list(self.models.keys())[0]
        table_models = self.models[table_name]
        
        # Regression predictions
        if "regression" in table_models:
            results["regression"] = {}
            
            for target, model in table_models["regression"].items():
                try:
                    # Predict
                    prediction_df = predict_reg(model, data=df)
                    predicted_value = prediction_df['prediction_label'].iloc[0]
                    
                    results["regression"][target] = {
                        "predicted_value": float(predicted_value),
                        "target": target
                    }
                    
                except Exception as e:
                    logger.error(f"Error in regression prediction for {target}: {str(e)}")
                    results["regression"][target] = {"error": str(e)}
        
        # Anomaly detection
        if "anomaly" in table_models:
            try:
                anomaly_df = predict_anom(table_models["anomaly"], data=df)
                
                # PyCaret anomaly: 1 = anomaly, 0 = normal
                is_anomaly = bool(anomaly_df['Anomaly'].iloc[0] == 1)
                anomaly_score = float(anomaly_df['Anomaly_Score'].iloc[0])
                
                results["anomaly"] = {
                    "is_anomaly": is_anomaly,
                    "anomaly_score": anomaly_score,
                    "threshold": 0.0
                }
                
            except Exception as e:
                logger.error(f"Error in anomaly detection: {str(e)}")
                results["anomaly"] = {"error": str(e)}
        
        return results
