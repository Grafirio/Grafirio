from django.core.management.base import BaseCommand
from messaging.consumers import start_request_consumer, start_response_consumer
import logging
import threading

logger = logging.getLogger(__name__)


class Command(BaseCommand):
    help = 'Starts the RabbitMQ consumers for AI requests and responses'

    def handle(self, *args, **options):
        self.stdout.write(self.style.SUCCESS('Starting AI Consumers...'))
        
        # Start request consumer in a separate thread
        request_thread = threading.Thread(target=self._start_request_consumer, daemon=True)
        request_thread.start()
        
        # Start response consumer in main thread
        try:
            self._start_response_consumer()
        except KeyboardInterrupt:
            self.stdout.write(self.style.WARNING('Consumers stopped by user'))
        except Exception as e:
            self.stdout.write(self.style.ERROR(f'Consumer error: {e}'))
            logger.error(f'Consumer error: {e}', exc_info=True)
    
    def _start_request_consumer(self):
        """Start the request consumer"""
        try:
            self.stdout.write(self.style.SUCCESS('Starting AI Request Consumer...'))
            start_request_consumer()
        except Exception as e:
            self.stdout.write(self.style.ERROR(f'Request consumer error: {e}'))
            logger.error(f'Request consumer error: {e}', exc_info=True)
    
    def _start_response_consumer(self):
        """Start the response consumer"""
        try:
            self.stdout.write(self.style.SUCCESS('Starting AI Response Consumer...'))
            start_response_consumer()
        except Exception as e:
            self.stdout.write(self.style.ERROR(f'Response consumer error: {e}'))
            logger.error(f'Response consumer error: {e}', exc_info=True)
