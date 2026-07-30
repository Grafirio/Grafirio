import { useEffect, useState, useCallback, useRef } from 'react';

const WS_URL = import.meta.env.VITE_DJANGO_AI_WS_URL || 'ws://localhost:8000';

/**
 * Custom hook for WebSocket connection to Django AI Service
 * @param {string} companyId - Company ID for real-time updates
 * @returns {Object} WebSocket state and methods
 */
export const useAIWebSocket = (companyId) => {
  const [isConnected, setIsConnected] = useState(false);
  const [lastMessage, setLastMessage] = useState(null);
  const [predictions, setPredictions] = useState([]);
  const [error, setError] = useState(null);
  const wsRef = useRef(null);
  const reconnectTimeoutRef = useRef(null);
  const reconnectAttemptsRef = useRef(0);

  const MAX_RECONNECT_ATTEMPTS = 5;
  const RECONNECT_DELAY = 3000;

  const connect = useCallback(() => {
    if (!companyId) {
      console.warn('No company ID provided for WebSocket connection');
      return;
    }

    try {
      const wsUrl = `${WS_URL}/ws/company/${companyId}/`;
      console.log('Connecting to WebSocket:', wsUrl);
      
      const ws = new WebSocket(wsUrl);
      wsRef.current = ws;

      ws.onopen = () => {
        console.log('WebSocket connected');
        setIsConnected(true);
        setError(null);
        reconnectAttemptsRef.current = 0;
      };

      ws.onmessage = (event) => {
        try {
          const data = JSON.parse(event.data);
          console.log('WebSocket message received:', data);
          
          setLastMessage(data);

          // Handle different message types
          if (data.type === 'prediction') {
            setPredictions(prev => [data.payload, ...prev].slice(0, 50)); // Keep last 50
          } else if (data.type === 'training_complete') {
            console.log('Model training completed:', data.payload);
          } else if (data.type === 'error') {
            setError(data.payload.message);
          }
        } catch (err) {
          console.error('Error parsing WebSocket message:', err);
        }
      };

      ws.onerror = (event) => {
        console.error('WebSocket error:', event);
        setError('WebSocket connection error');
      };

      ws.onclose = (event) => {
        console.log('WebSocket disconnected:', event.code, event.reason);
        setIsConnected(false);
        wsRef.current = null;

        // Auto-reconnect logic
        if (reconnectAttemptsRef.current < MAX_RECONNECT_ATTEMPTS) {
          reconnectAttemptsRef.current += 1;
          console.log(`Reconnecting... Attempt ${reconnectAttemptsRef.current}`);
          
          reconnectTimeoutRef.current = setTimeout(() => {
            connect();
          }, RECONNECT_DELAY);
        } else {
          setError('Max reconnection attempts reached');
        }
      };
    } catch (err) {
      console.error('Error creating WebSocket connection:', err);
      setError(err.message);
    }
  }, [companyId]);

  const disconnect = useCallback(() => {
    if (reconnectTimeoutRef.current) {
      clearTimeout(reconnectTimeoutRef.current);
    }

    if (wsRef.current) {
      wsRef.current.close();
      wsRef.current = null;
    }

    setIsConnected(false);
  }, []);

  const sendMessage = useCallback((message) => {
    if (wsRef.current && wsRef.current.readyState === WebSocket.OPEN) {
      wsRef.current.send(JSON.stringify(message));
      return true;
    } else {
      console.warn('WebSocket is not connected');
      return false;
    }
  }, []);

  const clearPredictions = useCallback(() => {
    setPredictions([]);
  }, []);

  // Auto-connect on mount
  useEffect(() => {
    connect();

    // Cleanup on unmount
    return () => {
      disconnect();
    };
  }, [connect, disconnect]);

  return {
    isConnected,
    lastMessage,
    predictions,
    error,
    connect,
    disconnect,
    sendMessage,
    clearPredictions
  };
};

/**
 * Hook for monitoring AI service health
 * @returns {Object} Health status of AI services
 */
export const useAIHealthStatus = () => {
  const [healthStatus, setHealthStatus] = useState({
    schemaAnalyzer: 'unknown',
    pycaretEngine: 'unknown',
    djangoAI: 'unknown'
  });

  useEffect(() => {
    const checkHealth = async () => {
      const services = [
        { name: 'schemaAnalyzer', url: 'http://localhost:8001' },
        { name: 'pycaretEngine', url: 'http://localhost:8002' },
        { name: 'djangoAI', url: 'http://localhost:8000' }
      ];

      const results = await Promise.allSettled(
        services.map(async (service) => {
          try {
            const response = await fetch(service.url, { method: 'GET' });
            return { name: service.name, status: response.ok ? 'healthy' : 'unhealthy' };
          } catch {
            return { name: service.name, status: 'offline' };
          }
        })
      );

      const newStatus = {};
      results.forEach((result) => {
        if (result.status === 'fulfilled') {
          newStatus[result.value.name] = result.value.status;
        }
      });

      setHealthStatus(newStatus);
    };

    checkHealth();
    const interval = setInterval(checkHealth, 30000); // Check every 30 seconds

    return () => clearInterval(interval);
  }, []);

  return healthStatus;
};
