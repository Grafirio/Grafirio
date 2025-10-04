from rest_framework import serializers


class GraphDataRequestSerializer(serializers.Serializer):
    """Grafik verisi talebi için serializer"""
    request_id = serializers.UUIDField(required=False)
    user_id = serializers.CharField(max_length=255)
    company_id = serializers.CharField(max_length=255)
    parameters = serializers.JSONField()


class QuestionRequestSerializer(serializers.Serializer):
    """Soru-cevap talebi için serializer"""
    request_id = serializers.UUIDField(required=False)
    user_id = serializers.CharField(max_length=255)
    company_id = serializers.CharField(max_length=255)
    question = serializers.CharField()
    context = serializers.ListField(child=serializers.CharField(), required=False)
