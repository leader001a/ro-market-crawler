# -*- coding: utf-8 -*-
"""
RO Market Crawler - Event-Driven Real-Time Architecture

Features:
- On-demand crawling (no scheduled background jobs)
- In-memory TTL caching (60s for search, 5min for top5)
- WebSocket real-time updates and subscriptions
- Automatic price history recording
"""
import json
import logging
import uuid
from contextlib import asynccontextmanager
from fastapi import FastAPI, WebSocket, WebSocketDisconnect
from fastapi.middleware.cors import CORSMiddleware

from src.config import settings
from src.crawler import GnjoyClient
from src.database import ItemRepository
from src.cache import item_cache, top5_cache
from src.websocket import ws_manager
from src.api import router
from src.api.routes import set_dependencies

# Configure logging
logging.basicConfig(
    level=logging.DEBUG if settings.DEBUG else logging.INFO,
    format="%(asctime)s - %(name)s - %(levelname)s - %(message)s",
)
logger = logging.getLogger(__name__)

# Global instances
client: GnjoyClient = None
repo: ItemRepository = None


@asynccontextmanager
async def lifespan(app: FastAPI):
    """Application lifespan manager."""
    global client, repo

    # Startup
    logger.info("Starting RO Market Crawler (Event-Driven Mode)...")

    # Initialize components
    client = GnjoyClient()
    repo = ItemRepository()
    await repo.connect()

    # Set dependencies for routes
    set_dependencies(client, repo)

    logger.info(f"Server running at http://{settings.HOST}:{settings.PORT}")
    logger.info(f"API docs: http://{settings.HOST}:{settings.PORT}/docs")
    logger.info(f"WebSocket: ws://{settings.HOST}:{settings.PORT}/ws")
    logger.info("Mode: On-Demand Crawling (no scheduler)")

    yield

    # Shutdown
    logger.info("Shutting down...")
    await client.close()
    await repo.close()
    logger.info("Shutdown complete")


# Create FastAPI app
app = FastAPI(
    title="RO Market Crawler",
    description="""
GNJOY Ragnarok Online item market data crawler API.

## Features
- **On-Demand Crawling**: Data fetched only when requested
- **Smart Caching**: 60s TTL for search, 5min for Top5
- **Real-Time WebSocket**: Subscribe to item updates
- **Price History**: Automatic recording of all searches

## WebSocket
Connect to `/ws` for real-time updates.

### Subscribe to items:
```json
{"action": "subscribe", "item_name": "엘더윌로우카드", "server_id": -1}
```

### Unsubscribe:
```json
{"action": "unsubscribe", "item_name": "엘더윌로우카드", "server_id": -1}
```
    """,
    version="0.2.0",
    lifespan=lifespan,
)

# CORS middleware
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

# Include API routes
app.include_router(router)


# ==================== WebSocket Endpoints ====================

@app.websocket("/ws")
async def websocket_endpoint(websocket: WebSocket):
    """
    WebSocket endpoint for real-time updates.

    Messages:
    - subscribe: {"action": "subscribe", "item_name": "...", "server_id": -1}
    - unsubscribe: {"action": "unsubscribe", "item_name": "...", "server_id": -1}
    - ping: {"action": "ping"}

    Server sends:
    - connected: On connection
    - item_update: When subscribed item has new data
    - top5_update: When top5 data updates
    - pong: Response to ping
    - error: On invalid message
    """
    # Generate unique client ID
    client_id = str(uuid.uuid4())[:8]

    # Connect
    conn = await ws_manager.connect(websocket, client_id)

    try:
        while True:
            # Receive message
            data = await websocket.receive_text()

            try:
                message = json.loads(data)
                action = message.get("action")

                if action == "subscribe":
                    item_name = message.get("item_name")
                    server_id = message.get("server_id", -1)

                    if not item_name:
                        await conn.send({"type": "error", "message": "item_name required"})
                        continue

                    await ws_manager.subscribe(client_id, item_name, server_id)
                    await conn.send({
                        "type": "subscribed",
                        "item_name": item_name,
                        "server_id": server_id,
                    })

                elif action == "unsubscribe":
                    item_name = message.get("item_name")
                    server_id = message.get("server_id", -1)

                    if not item_name:
                        await conn.send({"type": "error", "message": "item_name required"})
                        continue

                    await ws_manager.unsubscribe(client_id, item_name, server_id)
                    await conn.send({
                        "type": "unsubscribed",
                        "item_name": item_name,
                        "server_id": server_id,
                    })

                elif action == "ping":
                    await conn.send({"type": "pong"})

                elif action == "status":
                    info = await ws_manager.get_client_info(client_id)
                    await conn.send({
                        "type": "status",
                        "info": info,
                    })

                else:
                    await conn.send({
                        "type": "error",
                        "message": f"Unknown action: {action}",
                    })

            except json.JSONDecodeError:
                await conn.send({
                    "type": "error",
                    "message": "Invalid JSON",
                })

    except WebSocketDisconnect:
        await ws_manager.disconnect(client_id)


# ==================== Root Endpoints ====================

@app.get("/")
async def root():
    """Root endpoint with API information."""
    return {
        "name": "RO Market Crawler",
        "version": "0.2.0",
        "mode": "on-demand",
        "description": "GNJOY Ragnarok Online item market data API with real-time WebSocket",
        "features": [
            "On-demand crawling (no background scheduler)",
            "Smart TTL caching (60s search, 5min top5)",
            "WebSocket real-time updates",
            "Item subscription system",
            "Automatic price history",
        ],
        "endpoints": {
            "docs": "/docs",
            "websocket": "/ws",
            "servers": "/api/v1/servers",
            "top5": "/api/v1/items/top5",
            "search": "/api/v1/items/search?name={item_name}",
            "history": "/api/v1/items/history?name={item_name}",
            "stats": "/api/v1/stats",
            "health": "/api/v1/health",
        },
        "cache": {
            "search_ttl": "60 seconds",
            "top5_ttl": "300 seconds",
        },
    }


@app.get("/ws/stats")
async def websocket_stats():
    """Get WebSocket connection statistics."""
    return ws_manager.stats


def main():
    """Run the application with uvicorn."""
    import uvicorn

    uvicorn.run(
        "src.main:app",
        host=settings.HOST,
        port=settings.PORT,
        reload=settings.DEBUG,
    )


if __name__ == "__main__":
    main()
