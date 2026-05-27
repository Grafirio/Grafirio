from django.urls import path
from . import views

urlpatterns = [
    path('health/', views.health_check, name='health_check'),
    path('ai/graph-data/', views.request_graph_data, name='request_graph_data'),
    path('ai/question/', views.request_question_answer, name='request_question'),
    path('ai/chat/', views.chat_with_ai, name='chat_with_ai'),
]
