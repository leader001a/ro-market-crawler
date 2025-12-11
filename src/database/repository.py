# -*- coding: utf-8 -*-
"""SQLite database repository for item data."""
import aiosqlite
import logging
from datetime import datetime
from pathlib import Path
from typing import Optional

from src.config import settings
from src.models.item import (
    TopItem,
    DealItem,
    PriceHistory,
    RankHistory,
)

logger = logging.getLogger(__name__)


class ItemRepository:
    """Async SQLite repository for market data."""

    def __init__(self, db_path: Optional[Path] = None):
        self.db_path = db_path or settings.DATABASE_PATH
        self._connection: Optional[aiosqlite.Connection] = None

    async def connect(self):
        """Connect to database and create tables."""
        self.db_path.parent.mkdir(parents=True, exist_ok=True)
        self._connection = await aiosqlite.connect(self.db_path)
        self._connection.row_factory = aiosqlite.Row
        await self._create_tables()
        logger.info(f"Connected to database: {self.db_path}")

    async def close(self):
        """Close database connection."""
        if self._connection:
            await self._connection.close()
            logger.info("Database connection closed")

    async def _create_tables(self):
        """Create database tables if not exist."""
        await self._connection.executescript("""
            -- Price history for searched items
            CREATE TABLE IF NOT EXISTS price_history (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                item_id INTEGER,
                item_name TEXT NOT NULL,
                server_id INTEGER NOT NULL,
                price INTEGER NOT NULL,
                quantity INTEGER NOT NULL,
                shop_name TEXT,
                recorded_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
            );

            -- Rank history for top items
            CREATE TABLE IF NOT EXISTS rank_history (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                item_id INTEGER NOT NULL,
                item_name TEXT NOT NULL,
                category TEXT NOT NULL,
                rank INTEGER NOT NULL,
                deal_count INTEGER NOT NULL,
                recorded_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
            );

            -- Latest top items cache
            CREATE TABLE IF NOT EXISTS top_items_cache (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                item_id INTEGER NOT NULL,
                item_name TEXT NOT NULL,
                category TEXT NOT NULL,
                rank INTEGER NOT NULL,
                deal_count INTEGER NOT NULL,
                updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
            );

            -- Create indexes
            CREATE INDEX IF NOT EXISTS idx_price_item_name ON price_history(item_name);
            CREATE INDEX IF NOT EXISTS idx_price_server ON price_history(server_id);
            CREATE INDEX IF NOT EXISTS idx_price_recorded ON price_history(recorded_at);
            CREATE INDEX IF NOT EXISTS idx_rank_category ON rank_history(category);
            CREATE INDEX IF NOT EXISTS idx_rank_recorded ON rank_history(recorded_at);
            CREATE INDEX IF NOT EXISTS idx_cache_category ON top_items_cache(category);
        """)
        await self._connection.commit()

    # ==================== Top Items ====================

    async def save_top_items(self, items: list[TopItem], category: str):
        """Save top items to cache and history."""
        now = datetime.now().isoformat()

        # Clear cache for category
        await self._connection.execute(
            "DELETE FROM top_items_cache WHERE category = ?",
            (category,)
        )

        # Insert new cache entries and history
        for item in items:
            # Cache
            await self._connection.execute(
                """INSERT INTO top_items_cache
                   (item_id, item_name, category, rank, deal_count, updated_at)
                   VALUES (?, ?, ?, ?, ?, ?)""",
                (item.item_id, item.item_name, category,
                 item.rank_number, item.item_count, now)
            )

            # History
            await self._connection.execute(
                """INSERT INTO rank_history
                   (item_id, item_name, category, rank, deal_count, recorded_at)
                   VALUES (?, ?, ?, ?, ?, ?)""",
                (item.item_id, item.item_name, category,
                 item.rank_number, item.item_count, now)
            )

        await self._connection.commit()
        logger.info(f"Saved {len(items)} top items for category {category}")

    async def get_cached_top_items(self, category: Optional[str] = None) -> list[dict]:
        """Get cached top items, optionally filtered by category."""
        if category:
            cursor = await self._connection.execute(
                """SELECT * FROM top_items_cache
                   WHERE category = ?
                   ORDER BY rank ASC""",
                (category,)
            )
        else:
            cursor = await self._connection.execute(
                """SELECT * FROM top_items_cache
                   ORDER BY category, rank ASC"""
            )

        rows = await cursor.fetchall()
        return [dict(row) for row in rows]

    async def get_rank_history(
        self,
        item_id: int,
        limit: int = 100
    ) -> list[RankHistory]:
        """Get rank history for an item."""
        cursor = await self._connection.execute(
            """SELECT * FROM rank_history
               WHERE item_id = ?
               ORDER BY recorded_at DESC
               LIMIT ?""",
            (item_id, limit)
        )
        rows = await cursor.fetchall()
        return [RankHistory(**dict(row)) for row in rows]

    # ==================== Deal Items ====================

    async def save_deal_items(self, items: list[DealItem]):
        """Save deal items to price history."""
        now = datetime.now().isoformat()

        for item in items:
            await self._connection.execute(
                """INSERT INTO price_history
                   (item_name, server_id, price, quantity, shop_name, recorded_at)
                   VALUES (?, ?, ?, ?, ?, ?)""",
                (item.item_name, item.server_id, item.price,
                 item.quantity, item.shop_name, now)
            )

        await self._connection.commit()
        logger.info(f"Saved {len(items)} deal items to history")

    async def get_price_history(
        self,
        item_name: str,
        server_id: Optional[int] = None,
        limit: int = 100
    ) -> list[PriceHistory]:
        """Get price history for an item."""
        if server_id is not None and server_id != -1:
            cursor = await self._connection.execute(
                """SELECT * FROM price_history
                   WHERE item_name LIKE ? AND server_id = ?
                   ORDER BY recorded_at DESC
                   LIMIT ?""",
                (f"%{item_name}%", server_id, limit)
            )
        else:
            cursor = await self._connection.execute(
                """SELECT * FROM price_history
                   WHERE item_name LIKE ?
                   ORDER BY recorded_at DESC
                   LIMIT ?""",
                (f"%{item_name}%", limit)
            )

        rows = await cursor.fetchall()
        return [PriceHistory(**dict(row)) for row in rows]

    async def get_average_price(
        self,
        item_name: str,
        server_id: Optional[int] = None,
        days: int = 7
    ) -> Optional[float]:
        """Get average price for an item over specified days."""
        if server_id is not None and server_id != -1:
            cursor = await self._connection.execute(
                """SELECT AVG(price) as avg_price FROM price_history
                   WHERE item_name LIKE ? AND server_id = ?
                   AND recorded_at >= datetime('now', ?)""",
                (f"%{item_name}%", server_id, f"-{days} days")
            )
        else:
            cursor = await self._connection.execute(
                """SELECT AVG(price) as avg_price FROM price_history
                   WHERE item_name LIKE ?
                   AND recorded_at >= datetime('now', ?)""",
                (f"%{item_name}%", f"-{days} days")
            )

        row = await cursor.fetchone()
        return row["avg_price"] if row and row["avg_price"] else None

    # ==================== Statistics ====================

    async def get_stats(self) -> dict:
        """Get database statistics."""
        price_cursor = await self._connection.execute(
            "SELECT COUNT(*) as count FROM price_history"
        )
        rank_cursor = await self._connection.execute(
            "SELECT COUNT(*) as count FROM rank_history"
        )
        cache_cursor = await self._connection.execute(
            "SELECT MAX(updated_at) as last_update FROM top_items_cache"
        )

        price_count = (await price_cursor.fetchone())["count"]
        rank_count = (await rank_cursor.fetchone())["count"]
        last_update = (await cache_cursor.fetchone())["last_update"]

        return {
            "price_history_count": price_count,
            "rank_history_count": rank_count,
            "last_cache_update": last_update,
        }
