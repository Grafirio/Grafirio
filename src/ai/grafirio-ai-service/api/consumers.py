import json
from channels.generic.websocket import AsyncWebsocketConsumer


class AIConsumer(AsyncWebsocketConsumer):
    """
    WebSocket consumer for real-time AI response delivery
    Frontend connects: ws://localhost:8000/ws/ai/{user_id}/
    """
    
    async def connect(self):
        self.user_id = self.scope['url_route']['kwargs']['user_id']
        self.room_group_name = f'ai_user_{self.user_id}'
        
        # Join room group
        await self.channel_layer.group_add(
            self.room_group_name,
            self.channel_name
        )
        
        await self.accept()
        
        # Send connection confirmation
        await self.send(text_data=json.dumps({
            'type': 'connection_established',
            'message': f'Connected to AI service for user {self.user_id}'
        }))
    
    async def disconnect(self, close_code):
        # Leave room group
        await self.channel_layer.group_discard(
            self.room_group_name,
            self.channel_name
        )
    
    async def receive(self, text_data):
        """Handle messages from WebSocket (if needed)"""
        data = json.loads(text_data)
        # Handle ping/pong or other client messages
        if data.get('type') == 'ping':
            await self.send(text_data=json.dumps({
                'type': 'pong'
            }))
    
    # Handler for AI response messages
    async def ai_response(self, event):
        """
        Called when a message is sent from RabbitMQ consumer
        to this WebSocket connection via channel layer
        """
        await self.send(text_data=json.dumps({
            'type': 'ai_response',
            'data': event['data']
        }))


class CompanyDataConsumer(AsyncWebsocketConsumer):
    """
    WebSocket consumer for company-specific real-time data
    Frontend connects: ws://localhost:8000/ws/company/{company_id}/
    """
    
    async def connect(self):
        self.company_id = self.scope['url_route']['kwargs']['company_id']
        self.room_group_name = f'company_{self.company_id}'
        
        # Join company room
        await self.channel_layer.group_add(
            self.room_group_name,
            self.channel_name
        )
        
        await self.accept()
        
        # Send connection confirmation
        await self.send(text_data=json.dumps({
            'type': 'connection_established',
            'message': f'Connected to real-time data stream for company {self.company_id}'
        }))
    
    async def disconnect(self, close_code):
        # Leave room
        await self.channel_layer.group_discard(
            self.room_group_name,
            self.channel_name
        )
    
    async def receive(self, text_data):
        """Handle messages from client"""
        data = json.loads(text_data)
        
        if data.get('type') == 'ping':
            await self.send(text_data=json.dumps({'type': 'pong'}))
    
    # Handler for real-time data updates
    async def realtime_update(self, event):
        """
        Send real-time data update to frontend
        """
        await self.send(text_data=json.dumps({
            'type': 'realtime_update',
            'data': event['data']
        }))
