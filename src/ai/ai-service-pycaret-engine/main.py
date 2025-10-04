from fastapi import FastAPI, BackgroundTasks, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel
from typing import Dict, Optional, List, Any
import logging
import os
from dotenv import load_dotenv
from auto_trainer import AutoTrainer
from predictor import RealtimePredictor
import json

load_dotenv()

# Logging
logging.basicConfig(level=os.getenv("LOG_LEVEL", "INFO"))
logger = logging.getLogger(__name__)

app = FastAPI(
    title="Grafirio PyCaret Engine",
    description="Auto-ML training and real-time prediction service",
    version="1.0.0"
)

# CORS
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

# In-memory training status
training_status = {}


class DBConnection(BaseModel):
    type: str
    host: str
    port: int
    database: str
    username: str
    password: str


class TrainRequest(BaseModel):
    company_id: str
    db_connection: DBConnection
    semantic_schema: Dict


class PredictRequest(BaseModel):
    company_id: str
    table_name: str
    data: Dict[str, Any]


class TrainResponse(BaseModel):
    company_id: str
    status: str
    message: str
    task_id: Optional[str] = None


class PredictResponse(BaseModel):
    company_id: str
    table_name: str
    predictions: Dict[str, Any]
    status: str


@app.get("/")
async def root():
    return {
        "service": "PyCaret Engine",
        "status": "running",
        "version": "1.0.0"
    }


@app.get("/health")
async def health():
    return {"status": "healthy"}


@app.post("/train", response_model=TrainResponse)
async def train_models(request: TrainRequest, background_tasks: BackgroundTasks):
    """
    Train models for a company's database
    This is a long-running task executed in background
    """
    try:
        company_id = request.company_id
        
        # Check if already training
        if company_id in training_status and training_status[company_id]["status"] == "training":
            return TrainResponse(
                company_id=company_id,
                status="already_training",
                message="Models are already being trained for this company"
            )
        
        # Initialize status
        training_status[company_id] = {
            "status": "training",
            "progress": 0,
            "message": "Training started"
        }
        
        # Start training in background
        background_tasks.add_task(
            _train_company_models,
            company_id,
            request.db_connection,
            request.semantic_schema
        )
        
        logger.info(f"Training started for company: {company_id}")
        
        return TrainResponse(
            company_id=company_id,
            status="training_started",
            message="Model training started in background",
            task_id=company_id
        )
        
    except Exception as e:
        logger.error(f"Error starting training: {str(e)}")
        raise HTTPException(status_code=500, detail=str(e))


@app.get("/train/status/{company_id}")
async def get_training_status(company_id: str):
    """
    Get training status for a company
    """
    if company_id not in training_status:
        raise HTTPException(status_code=404, detail="Training status not found")
    
    return training_status[company_id]


@app.post("/predict", response_model=PredictResponse)
async def predict(request: PredictRequest):
    """
    Make predictions using trained models
    """
    try:
        predictor = RealtimePredictor()
        
        # Check if models exist
        if not predictor.models_exist(request.company_id, request.table_name):
            raise HTTPException(
                status_code=404,
                detail=f"No trained models found for {request.company_id}/{request.table_name}"
            )
        
        # Load models
        predictor.load_models(request.company_id, request.table_name)
        
        # Make predictions
        predictions = predictor.predict(request.data)
        
        logger.info(f"Prediction completed for {request.company_id}/{request.table_name}")
        
        return PredictResponse(
            company_id=request.company_id,
            table_name=request.table_name,
            predictions=predictions,
            status="success"
        )
        
    except HTTPException:
        raise
    except Exception as e:
        logger.error(f"Error making prediction: {str(e)}")
        raise HTTPException(status_code=500, detail=str(e))


@app.get("/models/{company_id}")
async def list_models(company_id: str):
    """
    List all trained models for a company
    """
    try:
        models_dir = os.path.join(os.getenv("MODEL_STORAGE_PATH", "/app/models"), company_id)
        
        if not os.path.exists(models_dir):
            return {"company_id": company_id, "models": []}
        
        # Read metadata
        metadata_path = os.path.join(models_dir, "metadata.json")
        
        if os.path.exists(metadata_path):
            with open(metadata_path, 'r') as f:
                metadata = json.load(f)
            return metadata
        else:
            return {"company_id": company_id, "models": []}
            
    except Exception as e:
        logger.error(f"Error listing models: {str(e)}")
        raise HTTPException(status_code=500, detail=str(e))


async def _train_company_models(company_id: str, db_connection: DBConnection, semantic_schema: Dict):
    """
    Background task for model training
    """
    try:
        logger.info(f"Starting model training for {company_id}")
        
        # Build connection string
        db_type = db_connection.type.lower()
        
        if db_type == "postgresql":
            connection_string = (
                f"postgresql://{db_connection.username}:{db_connection.password}"
                f"@{db_connection.host}:{db_connection.port}/{db_connection.database}"
            )
        elif db_type == "mysql":
            connection_string = (
                f"mysql+pymysql://{db_connection.username}:{db_connection.password}"
                f"@{db_connection.host}:{db_connection.port}/{db_connection.database}"
            )
        else:
            raise Exception(f"Unsupported database type: {db_type}")
        
        # Train models
        trainer = AutoTrainer(connection_string, semantic_schema, company_id)
        results = await trainer.train_all_tables()
        
        # Update status
        training_status[company_id] = {
            "status": "completed",
            "progress": 100,
            "message": "Training completed successfully",
            "results": results
        }
        
        logger.info(f"Training completed for {company_id}")
        
    except Exception as e:
        logger.error(f"Error in training task: {str(e)}")
        training_status[company_id] = {
            "status": "failed",
            "progress": 0,
            "message": f"Training failed: {str(e)}"
        }


if __name__ == "__main__":
    import uvicorn
    uvicorn.run(app, host="0.0.0.0", port=8002)
