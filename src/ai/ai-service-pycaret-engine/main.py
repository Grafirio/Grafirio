from fastapi import FastAPI, BackgroundTasks, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel
from typing import Dict, Optional, List, Any
import logging
import os
import threading
from dotenv import load_dotenv
from auto_trainer import AutoTrainer
from data_port import GatewayDataPort, SqlAlchemyDataPort
from predictor import RealtimePredictor
from agent_analyzer import AgentAnalyzer
import json
import re

load_dotenv()

# Logging
logging.basicConfig(level=os.getenv("LOG_LEVEL", "INFO"))
logger = logging.getLogger(__name__)


# Baglanti dizesi tasiyan hata metinlerinde sifreyi gizler. SQLAlchemy ve
# pyodbc, hata mesajina baglanti dizesinin tamamini koyuyor; log satiri
# oldugu gibi yazilirsa musteri veritabani sifresi Log Analytics'e dusuyor.
_SECRET_PATTERNS = [
    re.compile(r"(PWD=)([^;'\"]*)", re.IGNORECASE),
    re.compile(r"(password=)([^;'\"&]*)", re.IGNORECASE),
    re.compile(r"(://[^:/@]+:)([^@]*)(@)"),
]


def _redact_secrets(text: str) -> str:
    for pattern in _SECRET_PATTERNS:
        text = pattern.sub(
            lambda m: m.group(1) + "***" + (m.group(3) if m.lastindex and m.lastindex >= 3 else ""),
            text,
        )
    return text


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


class SeriesPoint(BaseModel):
    period: str
    value: float


class ForecastRequest(BaseModel):
    series: List[SeriesPoint]
    horizon: int = 12
    seasonality: Optional[int] = 12


class ForecastResponse(BaseModel):
    forecast: List[Dict[str, Any]]
    model_name: str
    status: str = "success"


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


@app.post("/forecast", response_model=ForecastResponse)
async def forecast(request: ForecastRequest):
    """Aylik zaman serisinden gelecek donem tahmini uretir (Holt-Winters/Holt/lineer trend)."""
    from forecaster import ForecastError, forecast_series

    try:
        result = forecast_series(
            [p.model_dump() for p in request.series],
            horizon=request.horizon,
            seasonality=request.seasonality,
        )
    except ForecastError as e:
        raise HTTPException(status_code=422, detail=str(e))
    except Exception as e:
        logger.error(f"Forecast hatasi: {e}")
        raise HTTPException(status_code=500, detail=f"Forecast uretilemedi: {e}")

    return ForecastResponse(forecast=result["forecast"], model_name=result["model_name"])


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
        
        # Bu yol kayitli bir SQL Server baglantisi degil, istekte gelen
        # Postgres/MySQL bilgileriyle calisiyor; dolayisiyla ic uctan
        # okunamiyor ve dogrudan baglanti kapisi kullaniliyor.
        trainer = AutoTrainer(
            SqlAlchemyDataPort(connection_string), semantic_schema, company_id)
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


# ========== Agent Analysis Endpoint ==========

class AgentAnalyzeRequest(BaseModel):
    request_id: str
    query_id: str
    company_id: str
    # Kimlik bilgisi degil, kayitli baglantinin kimligi geliyor. Veriyi
    # DataAnalysis.Api okuyor; bu servis ne host ne sifre goruyor.
    connection_id: str
    config_json: str
    analysis_params_json: str
    user_question: str


# In-memory agent analysis status
agent_analysis_status: Dict[str, Any] = {}


@app.post("/agent/analyze")
async def agent_analyze(request: AgentAnalyzeRequest, background_tasks: BackgroundTasks):
    """Run PyCaret agent analysis based on LLM-generated params"""
    try:
        query_id = request.query_id
        agent_analysis_status[query_id] = {
            "status": "processing",
            "progress": 0,
            "message": "Analysis started"
        }

        background_tasks.add_task(_run_agent_analysis, request)

        return {
            "query_id": query_id,
            "status": "processing",
            "message": "Agent analysis started"
        }
    except Exception as e:
        logger.error(f"Error starting agent analysis: {str(e)}")
        raise HTTPException(status_code=500, detail=str(e))


@app.get("/agent/analyze/status/{query_id}")
async def agent_analyze_status(query_id: str):
    """Get agent analysis status"""
    if query_id not in agent_analysis_status:
        raise HTTPException(status_code=404, detail="Analysis not found")
    return agent_analysis_status[query_id]


@app.get("/agent/analyze/result/{query_id}")
async def agent_analyze_result(query_id: str):
    """Get agent analysis result"""
    if query_id not in agent_analysis_status:
        raise HTTPException(status_code=404, detail="Analysis not found")

    status = agent_analysis_status[query_id]
    if status["status"] != "completed":
        return {"status": status["status"], "message": status.get("message", "")}

    return status


async def _run_agent_analysis(request: AgentAnalyzeRequest):
    """Background task for agent analysis"""
    query_id = request.query_id
    try:
        logger.info(f"Running agent analysis for query: {query_id}")

        # Veritabanina bu servis baglanmiyor: sorgu DataAnalysis.Api'ye
        # gonderiliyor, satirlar oradan geliyor. Musteri veritabani firewall
        # arkasindaysa aradaki fark orada kapaniyor; burasi degismiyor.
        config = json.loads(request.config_json)
        params = json.loads(request.analysis_params_json)

        agent_analysis_status[query_id] = {
            "status": "processing",
            "progress": 30,
            "message": "Loading data from database..."
        }

        analyzer = AgentAnalyzer(GatewayDataPort(request.connection_id))
        result = analyzer.run_analysis(config, params)

        if result["success"]:
            agent_analysis_status[query_id] = {
                "status": "completed",
                "progress": 100,
                "message": "Analysis completed",
                "query_id": query_id,
                "request_id": request.request_id,
                "company_id": request.company_id,
                "charts": result.get("charts", []),
                "insights": result.get("insights", []),
                "summary": result.get("summary", ""),
                # Denetim izi: hangi SQL calisti, hangi kolona gidildi, sonuc
                # tablonun tamamindan mi cikti. Analyzer bunu uretiyordu ama
                # burada birakiliyordu; "grafik dogru mu" sorusu bu olmadan
                # cevaplanamiyor.
                "audit": result.get("audit", {}),
            }
        else:
            agent_analysis_status[query_id] = {
                "status": "failed",
                "progress": 0,
                # Analyzer'in yazdigi gercek sebep ("X kolonu tabloda yok",
                # "hangi alana gore kirilacagi anlasilamadi") buraya gelir.
                # Onceden bu alan hic doldurulmadigi icin duzeltilebilir her
                # hata kullaniciya "Analysis failed" diye gorunuyordu.
                "message": result.get("error") or result.get("summary") or "Analysis failed",
                "query_id": query_id,
                "audit": result.get("audit", {}),
            }

        # Publish result to RabbitMQ if available
        _publish_result_to_rabbitmq(query_id, request)

        logger.info(f"Agent analysis completed for query: {query_id}")

    except Exception as e:
        # SQLAlchemy hata metnine bagIanti dizesini oldugu gibi koyuyor;
        # maskelenmezse musteri veritabani sifresi duz metin olarak loglara
        # (ve oradan Log Analytics'e) yaziliyor.
        logger.error(f"Error in agent analysis: {_redact_secrets(str(e))}")
        agent_analysis_status[query_id] = {
            "status": "failed",
            "progress": 0,
            "message": f"Analysis failed: {_redact_secrets(str(e))}",
            "query_id": query_id
        }


def _publish_result_to_rabbitmq(query_id: str, request: AgentAnalyzeRequest):
    """Publish analysis result back to RabbitMQ for .NET consumer"""
    try:
        import pika

        rabbitmq_host = os.getenv("RABBITMQ_HOST", "rabbitmq")
        rabbitmq_user = os.getenv("RABBITMQ_USER", "guest")
        rabbitmq_pass = os.getenv("RABBITMQ_PASS", "guest")

        credentials = pika.PlainCredentials(rabbitmq_user, rabbitmq_pass)
        connection = pika.BlockingConnection(
            pika.ConnectionParameters(host=rabbitmq_host, credentials=credentials)
        )
        channel = connection.channel()

        status = agent_analysis_status.get(query_id, {})

        message = {
            "requestId": request.request_id,
            "queryId": query_id,
            "companyId": request.company_id,
            "success": status.get("status") == "completed",
            "error": status.get("message", "") if status.get("status") == "failed" else None,
            "chartsJson": json.dumps(status.get("charts", [])),
            "insightsJson": json.dumps(status.get("insights", [])),
            "summary": status.get("summary", "")
        }

        channel.exchange_declare(exchange='pycaret-analysis-response', exchange_type='fanout', durable=True)
        channel.basic_publish(
            exchange='pycaret-analysis-response',
            routing_key='',
            body=json.dumps(message),
            properties=pika.BasicProperties(
                content_type='application/json',
                delivery_mode=2
            )
        )

        connection.close()
        logger.info(f"Published result to RabbitMQ for query: {query_id}")

    except Exception as e:
        logger.warning(f"Could not publish to RabbitMQ: {str(e)}")


# ========== RabbitMQ Consumer (Background Thread) ==========

def _start_rabbitmq_consumer():
    """Start a background RabbitMQ consumer for MassTransit messages"""
    try:
        import pika
        import time

        rabbitmq_host = os.getenv("RABBITMQ_HOST", "rabbitmq")
        rabbitmq_user = os.getenv("RABBITMQ_USER", "guest")
        rabbitmq_pass = os.getenv("RABBITMQ_PASS", "guest")

        # Retry connection
        for attempt in range(10):
            try:
                credentials = pika.PlainCredentials(rabbitmq_user, rabbitmq_pass)
                connection = pika.BlockingConnection(
                    pika.ConnectionParameters(
                        host=rabbitmq_host,
                        credentials=credentials,
                        heartbeat=600,
                        blocked_connection_timeout=300
                    )
                )
                break
            except Exception:
                logger.warning(f"RabbitMQ connection attempt {attempt + 1}/10 failed, retrying...")
                time.sleep(5)
        else:
            logger.error("Could not connect to RabbitMQ after 10 attempts")
            return

        channel = connection.channel()

        # Declare queue matching MassTransit convention
        queue_name = 'pycaret-analysis-request'
        channel.queue_declare(queue=queue_name, durable=True)
        channel.exchange_declare(exchange='pycaret-analysis-request', exchange_type='fanout', durable=True)
        channel.queue_bind(queue=queue_name, exchange='pycaret-analysis-request')

        def on_message(ch, method, properties, body):
            try:
                msg = json.loads(body)
                logger.info(f"Received MassTransit message: {msg.get('requestId', 'unknown')}")

                import asyncio
                request = AgentAnalyzeRequest(
                    request_id=msg.get("requestId", ""),
                    query_id=msg.get("queryId", ""),
                    company_id=msg.get("companyId", ""),
                    connection_id=msg.get("connectionId", ""),
                    config_json=msg.get("configJson", "{}"),
                    analysis_params_json=msg.get("analysisParamsJson", "{}"),
                    user_question=msg.get("userQuestion", "")
                )

                loop = asyncio.new_event_loop()
                asyncio.set_event_loop(loop)
                loop.run_until_complete(_run_agent_analysis(request))
                loop.close()

                ch.basic_ack(delivery_tag=method.delivery_tag)

            except Exception as e:
                logger.error(f"Error processing RabbitMQ message: {str(e)}")
                ch.basic_nack(delivery_tag=method.delivery_tag, requeue=False)

        channel.basic_qos(prefetch_count=1)
        channel.basic_consume(queue=queue_name, on_message_callback=on_message)

        logger.info("RabbitMQ consumer started, waiting for messages...")
        channel.start_consuming()

    except Exception as e:
        logger.error(f"RabbitMQ consumer error: {str(e)}")


@app.on_event("startup")
async def startup_event():
    """Start RabbitMQ consumer in background thread"""
    consumer_thread = threading.Thread(target=_start_rabbitmq_consumer, daemon=True)
    consumer_thread.start()
    logger.info("RabbitMQ consumer thread started")


if __name__ == "__main__":
    import uvicorn
    uvicorn.run(app, host="0.0.0.0", port=8002)
