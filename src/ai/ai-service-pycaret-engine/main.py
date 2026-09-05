"""Authenticated PyCaret HTTP entry point; agent jobs use the durable local queue."""

import asyncio
import json
import logging
import os
import re
from typing import Any, Dict, List, Optional

from dotenv import load_dotenv
from fastapi import BackgroundTasks, FastAPI, HTTPException
from pydantic import BaseModel

load_dotenv()

from agent_contract import Identifier  # noqa: E402
from agent_jobs import agent_lifespan, router  # noqa: E402
from internal_auth import InternalAuthMiddleware  # noqa: E402

logging.basicConfig(level=os.getenv("LOG_LEVEL", "INFO"))
logger = logging.getLogger(__name__)

app = FastAPI(title="Grafirio PyCaret Engine", version="1.0.0", lifespan=agent_lifespan)
app.add_middleware(InternalAuthMiddleware)
app.include_router(router)

# Legacy training is separate from the durable agent analysis queue.
training_status = {}


class DBConnection(BaseModel):
    type: str
    host: str
    port: int
    database: str
    username: str
    password: str


class TrainRequest(BaseModel):
    company_id: Identifier
    db_connection: DBConnection
    semantic_schema: Dict


class PredictRequest(BaseModel):
    company_id: Identifier
    table_name: str
    data: Dict[str, Any]


class SeriesPoint(BaseModel):
    period: str
    value: float


class ForecastRequest(BaseModel):
    series: List[SeriesPoint]
    horizon: int = 12
    seasonality: Optional[int] = 12


_SECRET_PATTERNS = [
    re.compile(r"(PWD=)([^;'\"]*)", re.IGNORECASE),
    re.compile(r"(password=)([^;'\"&]*)", re.IGNORECASE),
    re.compile(r"(://[^:/@]+:)([^@]*)(@)"),
]


def _redact_secrets(text):
    for pattern in _SECRET_PATTERNS:
        text = pattern.sub(lambda match: match.group(1) + "***" +
                           (match.group(3) if match.lastindex >= 3 else ""), text)
    return text


@app.get("/")
async def root():
    return {"service": "PyCaret Engine", "status": "running", "version": "1.0.0"}


@app.get("/health")
async def health():
    return {"status": "healthy"}


@app.post("/forecast")
def forecast(request: ForecastRequest):
    from forecaster import ForecastError, forecast_series
    try:
        result = forecast_series([point.model_dump() for point in request.series],
                                 horizon=request.horizon, seasonality=request.seasonality)
        return {**result, "status": "success"}
    except ForecastError as error:
        raise HTTPException(status_code=422, detail=str(error)) from error


@app.post("/train")
async def train_models(request: TrainRequest, background_tasks: BackgroundTasks):
    company_id = request.company_id
    if training_status.get(company_id, {}).get("status") == "training":
        return {"company_id": company_id, "status": "already_training", "message": "Models are already being trained for this company"}
    training_status[company_id] = {"status": "training", "progress": 0, "message": "Training started"}
    background_tasks.add_task(_train_company_models, request)
    return {"company_id": company_id, "status": "training_started", "message": "Model training started in background", "task_id": company_id}


def _train_company_models(request):
    from auto_trainer import AutoTrainer
    from data_port import SqlAlchemyDataPort
    from sqlalchemy.engine import URL
    try:
        database = request.db_connection
        drivers = {"postgresql": "postgresql", "mysql": "mysql+pymysql"}
        if database.type.lower() not in drivers:
            raise ValueError("Unsupported database type.")
        connection = URL.create(drivers[database.type.lower()], username=database.username,
                                password=database.password, host=database.host,
                                port=database.port, database=database.database)
        trainer = AutoTrainer(SqlAlchemyDataPort(connection), request.semantic_schema, request.company_id)
        results = asyncio.run(trainer.train_all_tables())
        training_status[request.company_id] = {"status": "completed", "progress": 100,
                                               "message": "Training completed successfully", "results": results}
    except Exception as error:
        logger.error("Training failed: %s", _redact_secrets(str(error)))
        training_status[request.company_id] = {"status": "failed", "progress": 0, "message": "Training failed."}


@app.get("/train/status/{company_id}")
async def get_training_status(company_id: Identifier):
    if company_id not in training_status:
        raise HTTPException(status_code=404, detail="Training status not found")
    return training_status[company_id]


@app.post("/predict")
def predict(request: PredictRequest):
    from predictor import RealtimePredictor
    predictor = RealtimePredictor()
    if not predictor.models_exist(request.company_id, request.table_name):
        raise HTTPException(status_code=404, detail="No trained models found.")
    predictor.load_models(request.company_id, request.table_name)
    return {"company_id": request.company_id, "table_name": request.table_name,
            "predictions": predictor.predict(request.data), "status": "success"}


@app.get("/models/{company_id}")
def list_models(company_id: Identifier):
    metadata_path = os.path.join(os.getenv("MODEL_STORAGE_PATH", "/app/models"), company_id, "metadata.json")
    if os.path.exists(metadata_path):
        with open(metadata_path, encoding="utf-8") as metadata:
            return json.load(metadata)
    return {"company_id": company_id, "models": []}


if __name__ == "__main__":
    import uvicorn
    uvicorn.run(app, host="0.0.0.0", port=8002)
