from django.core.management.base import BaseCommand
from messaging.realtime_consumer import start_realtime_consumer
import logging

logger = logging.getLogger(__name__)


class Command(BaseCommand):
    help = 'Starts the RabbitMQ consumer for real-time data processing'

    def handle(self, *args, **options):
        self.stdout.write(self.style.SUCCESS('Starting Realtime Data Consumer...'))
        try:
            start_realtime_consumer()
        except KeyboardInterrupt:
            self.stdout.write(self.style.WARNING('Consumer stopped by user'))
        except Exception as e:
            self.stdout.write(self.style.ERROR(f'Consumer error: {e}'))
            logger.error(f'Consumer error: {e}', exc_info=True)
