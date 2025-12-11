# -*- coding: utf-8 -*-
"""WebSocket connection and subscription manager."""
import asyncio
import json
import logging
from datetime import datetime
from typing import Dict, Set, Optional, Any
from dataclasses import dataclass, field
from fastapi import WebSocket, WebSocketDisconnect

logger = logging.getLogger(__name__)


@dataclass
class ClientConnection:
    """Represents a connected WebSocket client."""
    websocket: WebSocket
    client_id: str
    connected_at: datetime = field(default_factory=datetime.now)
    subscriptions: Set[str] = field(default_factory=set)

    async def send(self, data: dict) -> bool:
        """Send JSON data to client. Returns False if failed."""
        try:
            await self.websocket.send_json(data)
            return True
        except Exception as e:
            logger.warning(f"Failed to send to {self.client_id}: {e}")
            return False


class ConnectionManager:
    """
    Manages WebSocket connections and subscriptions.

    Features:
    - Connection lifecycle management
    - Item subscription system
    - Broadcast to subscribers
    - Connection statistics
    """

    def __init__(self):
        # client_id -> ClientConnection
        self._connections: Dict[str, ClientConnection] = {}
        # subscription_key -> set of client_ids
        self._subscriptions: Dict[str, Set[str]] = {}
        self._lock = asyncio.Lock()
        self._message_count = 0

    async def connect(self, websocket: WebSocket, client_id: str) -> ClientConnection:
        """Accept and register a new WebSocket connection."""
        await websocket.accept()

        async with self._lock:
            # Disconnect existing connection with same ID
            if client_id in self._connections:
                await self._disconnect_client(client_id)

            client = ClientConnection(
                websocket=websocket,
                client_id=client_id,
            )
            self._connections[client_id] = client

        logger.info(f"Client connected: {client_id} (total: {len(self._connections)})")

        # Send welcome message
        await client.send({
            "type": "connected",
            "client_id": client_id,
            "timestamp": datetime.now().isoformat(),
        })

        return client

    async def disconnect(self, client_id: str) -> None:
        """Disconnect and cleanup a client."""
        async with self._lock:
            await self._disconnect_client(client_id)

    async def _disconnect_client(self, client_id: str) -> None:
        """Internal disconnect (must hold lock)."""
        if client_id not in self._connections:
            return

        client = self._connections[client_id]

        # Remove from all subscriptions
        for sub_key in client.subscriptions:
            if sub_key in self._subscriptions:
                self._subscriptions[sub_key].discard(client_id)
                if not self._subscriptions[sub_key]:
                    del self._subscriptions[sub_key]

        del self._connections[client_id]
        logger.info(f"Client disconnected: {client_id} (total: {len(self._connections)})")

    async def subscribe(
        self,
        client_id: str,
        item_name: str,
        server_id: int = -1
    ) -> bool:
        """
        Subscribe client to item updates.

        Args:
            client_id: Client identifier
            item_name: Item name to subscribe
            server_id: Server filter (-1 for all)

        Returns:
            True if subscription successful
        """
        sub_key = self._make_sub_key(item_name, server_id)

        async with self._lock:
            if client_id not in self._connections:
                return False

            client = self._connections[client_id]

            # Add to subscription
            if sub_key not in self._subscriptions:
                self._subscriptions[sub_key] = set()

            self._subscriptions[sub_key].add(client_id)
            client.subscriptions.add(sub_key)

        logger.debug(f"Client {client_id} subscribed to {sub_key}")
        return True

    async def unsubscribe(
        self,
        client_id: str,
        item_name: str,
        server_id: int = -1
    ) -> bool:
        """Unsubscribe client from item updates."""
        sub_key = self._make_sub_key(item_name, server_id)

        async with self._lock:
            if client_id not in self._connections:
                return False

            client = self._connections[client_id]

            # Remove from subscription
            if sub_key in self._subscriptions:
                self._subscriptions[sub_key].discard(client_id)
                if not self._subscriptions[sub_key]:
                    del self._subscriptions[sub_key]

            client.subscriptions.discard(sub_key)

        logger.debug(f"Client {client_id} unsubscribed from {sub_key}")
        return True

    async def broadcast_item_update(
        self,
        item_name: str,
        server_id: int,
        data: Any
    ) -> int:
        """
        Broadcast item update to all subscribers.

        Args:
            item_name: Item name
            server_id: Server ID
            data: Update data to send

        Returns:
            Number of clients notified
        """
        # Check both specific server and all-server subscriptions
        sub_keys = [
            self._make_sub_key(item_name, server_id),
            self._make_sub_key(item_name, -1),  # All servers
        ]

        notified_clients: Set[str] = set()

        async with self._lock:
            for sub_key in sub_keys:
                if sub_key in self._subscriptions:
                    notified_clients.update(self._subscriptions[sub_key])

        # Send to all notified clients
        message = {
            "type": "item_update",
            "item_name": item_name,
            "server_id": server_id,
            "data": data,
            "timestamp": datetime.now().isoformat(),
        }

        sent_count = 0
        failed_clients = []

        for client_id in notified_clients:
            async with self._lock:
                client = self._connections.get(client_id)

            if client:
                success = await client.send(message)
                if success:
                    sent_count += 1
                else:
                    failed_clients.append(client_id)

        # Cleanup failed connections
        for client_id in failed_clients:
            await self.disconnect(client_id)

        self._message_count += sent_count
        logger.debug(f"Broadcast {item_name}: {sent_count} clients notified")

        return sent_count

    async def broadcast_all(self, message: dict) -> int:
        """Broadcast message to all connected clients."""
        async with self._lock:
            clients = list(self._connections.values())

        sent_count = 0
        failed_clients = []

        for client in clients:
            success = await client.send(message)
            if success:
                sent_count += 1
            else:
                failed_clients.append(client.client_id)

        for client_id in failed_clients:
            await self.disconnect(client_id)

        self._message_count += sent_count
        return sent_count

    def _make_sub_key(self, item_name: str, server_id: int) -> str:
        """Create subscription key from item name and server."""
        return f"{item_name.lower()}:{server_id}"

    @property
    def stats(self) -> dict:
        """Get connection statistics."""
        return {
            "connections": len(self._connections),
            "subscriptions": len(self._subscriptions),
            "total_messages_sent": self._message_count,
        }

    @property
    def connection_count(self) -> int:
        return len(self._connections)

    async def get_client_info(self, client_id: str) -> Optional[dict]:
        """Get information about a specific client."""
        async with self._lock:
            client = self._connections.get(client_id)
            if not client:
                return None

            return {
                "client_id": client.client_id,
                "connected_at": client.connected_at.isoformat(),
                "subscriptions": list(client.subscriptions),
            }


# Global WebSocket manager instance
ws_manager = ConnectionManager()
