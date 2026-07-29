import pika
import json
import logging
import os
from dotenv import load_dotenv
from schema_analyzer import SchemaAnalyzer
import requests
from datetime import datetime, date
from decimal import Decimal
import sqlalchemy
from sqlalchemy import text

from llm_client import LLMClient
from urllib.parse import quote_plus

load_dotenv()

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)


class QueryExecutor:
    def __init__(self, connection_string: str):
        self.engine = sqlalchemy.create_engine(connection_string)
        
        # LLM saglayicisi LLM_PROVIDER ile secilir (azure_openai | gemini)
        client = LLMClient()
        self.model = client if client.available else None
        if self.model is None:
            logger.warning("LLM yapilandirilmamis")
    
    def natural_language_to_sql(self, question: str, schema_info: dict) -> str:
        """Convert natural language question to SQL using Gemini"""
        
        if not self.model:
            # Mock SQL for testing
            return "SELECT TOP 10 Name, Price FROM Products ORDER BY Price DESC"
        
        # Prepare schema for prompt
        tables_desc = "\n".join([
            f"{table}: {', '.join([col for col in info.get('columns', [])])}"
            for table, info in schema_info.items()
        ])
        
        prompt = f"""
Veritabanı şeması:
{tables_desc}

Kullanıcı sorusu: "{question}"

Bu soruyu cevaplamak için gerekli SQL sorgusunu yaz.
- Microsoft SQL Server syntax kullan
- TOP 10 ile sınırla (gerekirse)
- Sadece SQL sorgusunu döndür, başka açıklama yapma
- SELECT, JOIN, WHERE, GROUP BY, ORDER BY kullan

SQL:
"""
        
        try:
            sql = self.model.generate(
                prompt, temperature=0.2, max_tokens=512
            ).strip()
            
            # Clean up markdown if present
            if sql.startswith('```sql'):
                sql = sql[6:]
            if sql.startswith('```'):
                sql = sql[3:]
            if sql.endswith('```'):
                sql = sql[:-3]
            
            return sql.strip()
            
        except Exception as e:
            logger.error(f"Error generating SQL: {e}")
            raise
    
    def execute_sql(self, sql: str) -> dict:
        """Execute SQL query and return results"""
        try:
            with self.engine.connect() as conn:
                result = conn.execute(text(sql))
                
                # Get column names
                columns = list(result.keys())
                
                # Fetch rows and convert to JSON-serializable types
                rows = []
                for row in result:
                    row_dict = {}
                    for col, val in zip(columns, row):
                        # Convert Decimal to float
                        if isinstance(val, Decimal):
                            row_dict[col] = float(val)
                        # Convert datetime/date to ISO string
                        elif isinstance(val, (datetime, date)):
                            row_dict[col] = val.isoformat()
                        else:
                            row_dict[col] = val
                    rows.append(row_dict)
                
                return {
                    "columns": columns,
                    "rows": rows,
                    "rowCount": len(rows)
                }
                
        except Exception as e:
            logger.error(f"SQL execution error: {e}")
            raise
    
    def answer_question(self, question: str, database: str, tables: list) -> dict:
        """Answer a natural language question about the database"""
        try:
            logger.info(f"Answering question: {question}")
            
            # Get schema info (simplified)
            analyzer = SchemaAnalyzer(self.engine.url)
            schema_info = {}
            
            # Analyze specified tables or all tables
            if tables:
                for table in tables:
                    try:
                        schema_info[table] = analyzer._analyze_table(table)
                    except:
                        pass
            
            # Convert question to SQL
            sql = self.natural_language_to_sql(question, schema_info)
            logger.info(f"Generated SQL: {sql}")
            
            # Execute SQL
            query_result = self.execute_sql(sql)
            
            # Format response
            return {
                "success": True,
                "question": question,
                "sql": sql,
                "columns": query_result["columns"],
                "data": query_result["rows"],
                "rowCount": query_result["rowCount"]
            }
            
        except Exception as e:
            logger.error(f"Error answering question: {e}")
            return {
                "success": False,
                "question": question,
                "error": str(e)
            }


class QueryConsumer:
    def __init__(self):
        self.rabbitmq_host = os.getenv('RABBITMQ_HOST', 'localhost')
        self.rabbitmq_port = int(os.getenv('RABBITMQ_PORT', 5672))
        self.rabbitmq_user = os.getenv('RABBITMQ_USER', 'guest')
        self.rabbitmq_password = os.getenv('RABBITMQ_PASSWORD', 'guest123')
        self.backend_api_url = os.getenv('BACKEND_API_URL', 'http://data-analysis.api.container:5221')
        
        self.connection = None
        self.channel = None
    
    def connect(self):
        """Connect to RabbitMQ"""
        try:
            credentials = pika.PlainCredentials(self.rabbitmq_user, self.rabbitmq_password)
            parameters = pika.ConnectionParameters(
                host=self.rabbitmq_host,
                port=self.rabbitmq_port,
                credentials=credentials,
                heartbeat=600,
                blocked_connection_timeout=300
            )
            
            self.connection = pika.BlockingConnection(parameters)
            self.channel = self.connection.channel()
            
            # Declare exchange and queue for AI questions
            self.channel.exchange_declare(exchange='ai.requests', exchange_type='topic', durable=True)
            self.channel.queue_declare(queue='ai.question.queue', durable=True)
            self.channel.queue_bind(exchange='ai.requests', queue='ai.question.queue', routing_key='ai.request.question')
            
            logger.info(f"✅ Query Consumer connected to RabbitMQ: {self.rabbitmq_host}:{self.rabbitmq_port}")
            
        except Exception as e:
            logger.error(f"❌ Failed to connect to RabbitMQ: {e}")
            raise
    
    def send_result(self, request_id: str, result: dict):
        """Send query result back to API"""
        try:
            url = f"{self.backend_api_url}/api/ai/query-result"
            payload = {
                "RequestId": request_id,      # PascalCase for C#
                "Status": "completed" if result.get("success") else "failed",
                "Result": json.dumps(result),  # Convert to JSON string instead of dynamic
                "CompletedAt": datetime.utcnow().isoformat()
            }
            
            response = requests.post(url, json=payload, timeout=10)
            
            if response.ok:
                logger.info(f"✅ Query result sent for {request_id}")
            else:
                logger.error(f"❌ Failed to send result: {response.status_code}")
                logger.error(f"❌ Response body: {response.text}")
                logger.error(f"❌ Request payload: {json.dumps(payload, indent=2)}")
                
        except Exception as e:
            logger.error(f"❌ Error sending result: {e}")
    
    def process_question(self, ch, method, properties, body):
        """Process AI question"""
        try:
            message = json.loads(body)
            logger.info(f"📥 Received question: {message.get('question')}")
            
            request_id = message.get('request_id')
            question = message.get('question')
            database = message.get('database')
            tables = message.get('tables', [])
            
            # Build connection string (SQL Server)
            host = message.get('host', 'grafirio-sqlserver-test')  # Use host from message
            port = message.get('port', 1433)
            username = message.get('username', 'sa')
            password = 'Test123!@#'  # Test server password (hardcoded for now)
            
            # URL encode password to handle special characters
            encoded_password = quote_plus(password)
            
            connection_string = (
                f"mssql+pymssql://{username}:{encoded_password}"
                f"@{host}:{port}/{database}"
            )
            
            # Execute query
            executor = QueryExecutor(connection_string)
            result = executor.answer_question(question, database, tables)
            
            # Send result back
            self.send_result(request_id, result)
            
            # Acknowledge
            ch.basic_ack(delivery_tag=method.delivery_tag)
            
        except Exception as e:
            logger.error(f"❌ Error processing question: {e}")
            ch.basic_nack(delivery_tag=method.delivery_tag, requeue=False)
    
    def start_consuming(self):
        """Start listening for questions"""
        try:
            self.channel.basic_qos(prefetch_count=1)
            self.channel.basic_consume(
                queue='ai.question.queue',
                on_message_callback=self.process_question
            )
            
            logger.info("🚀 Query Consumer started. Waiting for questions...")
            self.channel.start_consuming()
            
        except KeyboardInterrupt:
            logger.info("🛑 Shutting down...")
            self.stop()
    
    def stop(self):
        """Stop consumer"""
        if self.channel and self.channel.is_open:
            self.channel.stop_consuming()
        if self.connection and self.connection.is_open:
            self.connection.close()


def main():
    consumer = QueryConsumer()
    consumer.connect()
    consumer.start_consuming()


if __name__ == "__main__":
    main()
