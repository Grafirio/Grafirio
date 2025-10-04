from celery import shared_task
import pandas as pd
import sqlalchemy
import redis
import logging
from datetime import datetime
from messaging.publishers import rabbitmq_client
import json

logger = logging.getLogger(__name__)

# Redis client for tracking last sync times
redis_client = redis.Redis(
    host='localhost',
    port=6379,
    db=0,
    decode_responses=True
)


@shared_task
def poll_company_data(company_id: str, db_connection: dict, semantic_schema: dict):
    """
    Celery periodic task to poll for new data from company database
    Runs every 30 seconds for each company
    """
    try:
        logger.info(f"Polling data for company: {company_id}")
        
        # Build connection string
        db_type = db_connection['type'].lower()
        
        if db_type == 'postgresql':
            connection_string = (
                f"postgresql://{db_connection['username']}:{db_connection['password']}"
                f"@{db_connection['host']}:{db_connection['port']}/{db_connection['database']}"
            )
        elif db_type == 'mysql':
            connection_string = (
                f"mysql+pymysql://{db_connection['username']}:{db_connection['password']}"
                f"@{db_connection['host']}:{db_connection['port']}/{db_connection['database']}"
            )
        else:
            logger.warning(f"Unsupported DB type: {db_type}")
            return
        
        engine = sqlalchemy.create_engine(connection_string)
        
        # Process each table
        tables_config = semantic_schema.get('tables', {})
        
        for table_name, table_config in tables_config.items():
            try:
                timestamp_column = table_config.get('timestamp_column')
                
                if not timestamp_column:
                    logger.warning(f"No timestamp column for {table_name}, skipping real-time sync")
                    continue
                
                # Get last sync time
                last_sync_key = f"last_sync:{company_id}:{table_name}"
                last_sync = redis_client.get(last_sync_key)
                
                if not last_sync:
                    # First sync: get last 100 records
                    query = f"""
                        SELECT * FROM {table_name}
                        ORDER BY {timestamp_column} DESC
                        LIMIT 100
                    """
                else:
                    # Get new records since last sync
                    query = f"""
                        SELECT * FROM {table_name}
                        WHERE {timestamp_column} > '{last_sync}'
                        ORDER BY {timestamp_column} ASC
                    """
                
                # Execute query
                df = pd.read_sql(query, engine)
                
                if df.empty:
                    logger.debug(f"No new data for {company_id}/{table_name}")
                    continue
                
                logger.info(f"Found {len(df)} new records for {company_id}/{table_name}")
                
                # Process each new record
                for idx, row in df.iterrows():
                    record = row.to_dict()
                    
                    # Convert timestamps to strings
                    for key, value in record.items():
                        if pd.isna(value):
                            record[key] = None
                        elif isinstance(value, pd.Timestamp):
                            record[key] = value.isoformat()
                    
                    # Publish to RabbitMQ
                    message = {
                        'company_id': company_id,
                        'table_name': table_name,
                        'data': record,
                        'timestamp': datetime.utcnow().isoformat()
                    }
                    
                    rabbitmq_client.publish_message(
                        exchange='data.realtime',
                        routing_key='data.new',
                        message=message
                    )
                
                # Update last sync time
                if not df.empty and timestamp_column in df.columns:
                    latest_timestamp = df[timestamp_column].max()
                    redis_client.set(last_sync_key, str(latest_timestamp))
                    logger.info(f"Updated last_sync for {table_name}: {latest_timestamp}")
                
            except Exception as e:
                logger.error(f"Error polling table {table_name}: {str(e)}")
        
        engine.dispose()
        
    except Exception as e:
        logger.error(f"Error in poll_company_data for {company_id}: {str(e)}")


@shared_task
def start_polling_for_company(company_id: str, db_connection: dict, semantic_schema: dict, interval_seconds: int = 30):
    """
    Start periodic polling for a company
    """
    from celery import current_app
    
    # Schedule periodic task
    current_app.add_periodic_task(
        interval_seconds,
        poll_company_data.s(company_id, db_connection, semantic_schema),
        name=f'poll-{company_id}'
    )
    
    logger.info(f"Started polling for {company_id} with {interval_seconds}s interval")
