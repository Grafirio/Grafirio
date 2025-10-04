from .rabbitmq_client import rabbitmq_client


def publish_graph_data_request(message):
    """
    Grafik verisi talebi mesajını AI servisine gönder
    Routing key: ai.request.graph
    """
    rabbitmq_client.publish_message(
        exchange='ai.requests',
        routing_key='ai.request.graph',
        message=message
    )


def publish_question_request(message):
    """
    Soru-cevap talebi mesajını AI servisine gönder
    Routing key: ai.request.question
    """
    rabbitmq_client.publish_message(
        exchange='ai.requests',
        routing_key='ai.request.question',
        message=message
    )
