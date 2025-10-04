from django.urls import re_path
from . import consumers

websocket_urlpatterns = [
    re_path(r'ws/ai/(?P<user_id>\w+)/$', consumers.AIConsumer.as_asgi()),
    re_path(r'ws/company/(?P<company_id>[\w-]+)/$', consumers.CompanyDataConsumer.as_asgi()),
]
