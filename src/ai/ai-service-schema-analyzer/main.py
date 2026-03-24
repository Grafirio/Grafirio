from fastapi import FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel
from typing import Dict, Optional, List
import logging
from schema_analyzer import SchemaAnalyzer
import os
from dotenv import load_dotenv

load_dotenv()

# Logging
logging.basicConfig(level=os.getenv("LOG_LEVEL", "INFO"))
logger = logging.getLogger(__name__)

app = FastAPI(
    title="Grafirio Schema Analyzer Service",
    description="AI-powered database schema analysis and understanding",
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


class DBConnection(BaseModel):
    type: str  # postgresql, mysql, mssql
    host: str
    port: int
    database: str
    username: str
    password: str


class AnalyzeRequest(BaseModel):
    company_id: str
    company_name: str
    db_connection: DBConnection


class AnalyzeResponse(BaseModel):
    company_id: str
    status: str
    semantic_schema: Optional[Dict] = None
    unclear_fields: Optional[List[Dict]] = None
    error: Optional[str] = None


@app.get("/")
async def root():
    return {
        "service": "Schema Analyzer",
        "status": "running",
        "version": "1.0.0"
    }


@app.get("/health")
async def health():
    return {"status": "healthy"}


@app.post("/analyze", response_model=AnalyzeResponse)
async def analyze_schema(request: AnalyzeRequest):
    """
    Analyze database schema and generate semantic understanding
    """
    try:
        logger.info(f"Starting schema analysis for company: {request.company_id}")
        
        # Build connection string
        db_type = request.db_connection.type.lower()
        
        if db_type == "postgresql":
            connection_string = (
                f"postgresql://{request.db_connection.username}:{request.db_connection.password}"
                f"@{request.db_connection.host}:{request.db_connection.port}/{request.db_connection.database}"
            )
        elif db_type == "mysql":
            connection_string = (
                f"mysql+pymysql://{request.db_connection.username}:{request.db_connection.password}"
                f"@{request.db_connection.host}:{request.db_connection.port}/{request.db_connection.database}"
            )
        elif db_type == "mssql":
            connection_string = (
                f"mssql+pymssql://{request.db_connection.username}:{request.db_connection.password}"
                f"@{request.db_connection.host}:{request.db_connection.port}/{request.db_connection.database}"
            )
        else:
            raise HTTPException(status_code=400, detail=f"Unsupported database type: {db_type}")
        
        # Analyze schema
        analyzer = SchemaAnalyzer(connection_string)
        result = analyzer.analyze_database(request.company_name)
        
        logger.info(f"Schema analysis completed for: {request.company_id}")
        
        return AnalyzeResponse(
            company_id=request.company_id,
            status="success",
            semantic_schema=result.get("semantic_schema"),
            unclear_fields=result.get("unclear_fields", [])
        )
        
    except Exception as e:
        logger.error(f"Error analyzing schema for {request.company_id}: {str(e)}")
        return AnalyzeResponse(
            company_id=request.company_id,
            status="error",
            error=str(e)
        )


@app.post("/test-connection")
async def test_connection(db_connection: DBConnection):
    """
    Test database connection
    """
    try:
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
        elif db_type == "mssql":
            connection_string = (
                f"mssql+pymssql://{db_connection.username}:{db_connection.password}"
                f"@{db_connection.host}:{db_connection.port}/{db_connection.database}"
            )
        else:
            raise HTTPException(status_code=400, detail=f"Unsupported database type: {db_type}")
        
        from sqlalchemy import create_engine
        engine = create_engine(connection_string)
        
        # Test connection
        with engine.connect() as conn:
            conn.execute("SELECT 1")
        
        return {
            "status": "success",
            "message": "Database connection successful"
        }
        
    except Exception as e:
        logger.error(f"Connection test failed: {str(e)}")
        raise HTTPException(status_code=400, detail=f"Connection failed: {str(e)}")


if __name__ == "__main__":
    import uvicorn
    uvicorn.run(app, host="0.0.0.0", port=8001)
