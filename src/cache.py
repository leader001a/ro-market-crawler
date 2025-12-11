# -*- coding: utf-8 -*-
"""In-memory TTL cache for on-demand crawling."""
import asyncio
import logging
from datetime import datetime, timedelta
from typing import Any, Optional, Dict, Callable
from dataclasses import dataclass, field

logger = logging.getLogger(__name__)


@dataclass
class CacheEntry:
    """Single cache entry with TTL."""
    value: Any
    expires_at: datetime
    created_at: datetime = field(default_factory=datetime.now)

    @property
    def is_expired(self) -> bool:
        return datetime.now() >= self.expires_at

    @property
    def ttl_remaining(self) -> float:
        """Remaining TTL in seconds."""
        delta = self.expires_at - datetime.now()
        return max(0, delta.total_seconds())


class TTLCache:
    """
    Thread-safe in-memory cache with TTL expiration.

    Features:
    - Automatic expiration
    - Cache statistics
    - Async-safe operations
    """

    def __init__(self, default_ttl: int = 60):
        """
        Initialize cache.

        Args:
            default_ttl: Default TTL in seconds (default: 60)
        """
        self._cache: Dict[str, CacheEntry] = {}
        self._default_ttl = default_ttl
        self._lock = asyncio.Lock()
        self._stats = {"hits": 0, "misses": 0, "sets": 0}

    async def get(self, key: str) -> Optional[Any]:
        """
        Get value from cache.

        Returns None if key doesn't exist or is expired.
        """
        async with self._lock:
            entry = self._cache.get(key)

            if entry is None:
                self._stats["misses"] += 1
                return None

            if entry.is_expired:
                del self._cache[key]
                self._stats["misses"] += 1
                return None

            self._stats["hits"] += 1
            return entry.value

    async def set(
        self,
        key: str,
        value: Any,
        ttl: Optional[int] = None
    ) -> None:
        """
        Set value in cache with TTL.

        Args:
            key: Cache key
            value: Value to store
            ttl: TTL in seconds (uses default if not specified)
        """
        ttl = ttl or self._default_ttl
        expires_at = datetime.now() + timedelta(seconds=ttl)

        async with self._lock:
            self._cache[key] = CacheEntry(value=value, expires_at=expires_at)
            self._stats["sets"] += 1

    async def delete(self, key: str) -> bool:
        """Delete key from cache. Returns True if key existed."""
        async with self._lock:
            if key in self._cache:
                del self._cache[key]
                return True
            return False

    async def clear(self) -> int:
        """Clear all cache entries. Returns count of cleared entries."""
        async with self._lock:
            count = len(self._cache)
            self._cache.clear()
            return count

    async def cleanup_expired(self) -> int:
        """Remove all expired entries. Returns count of removed entries."""
        async with self._lock:
            expired_keys = [
                k for k, v in self._cache.items() if v.is_expired
            ]
            for key in expired_keys:
                del self._cache[key]
            return len(expired_keys)

    async def get_or_set(
        self,
        key: str,
        factory: Callable,
        ttl: Optional[int] = None
    ) -> Any:
        """
        Get from cache or compute and store.

        Args:
            key: Cache key
            factory: Async function to compute value if not cached
            ttl: TTL in seconds

        Returns:
            Cached or computed value
        """
        value = await self.get(key)
        if value is not None:
            return value

        # Compute new value
        if asyncio.iscoroutinefunction(factory):
            value = await factory()
        else:
            value = factory()

        if value is not None:
            await self.set(key, value, ttl)

        return value

    @property
    def stats(self) -> dict:
        """Get cache statistics."""
        total = self._stats["hits"] + self._stats["misses"]
        hit_rate = (self._stats["hits"] / total * 100) if total > 0 else 0

        return {
            **self._stats,
            "size": len(self._cache),
            "hit_rate": f"{hit_rate:.1f}%",
        }

    def __len__(self) -> int:
        return len(self._cache)


# Global cache instance
item_cache = TTLCache(default_ttl=60)  # 1분 TTL
top5_cache = TTLCache(default_ttl=300)  # 5분 TTL for Top5
