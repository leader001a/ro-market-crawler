# -*- coding: utf-8 -*-
"""FastAPI routes for RO Market API with on-demand crawling."""
import logging
from typing import Optional
from fastapi import APIRouter, HTTPException, Query, Depends

from src.config import settings
from src.crawler import GnjoyClient
from src.database import ItemRepository
from src.cache import item_cache, top5_cache
from src.websocket import ws_manager
from src.models.item import (
    ItemSearchResponse,
    Top5CategoryResponse,
    ItemCategory,
)

logger = logging.getLogger(__name__)
router = APIRouter(prefix="/api/v1", tags=["market"])

# Global instances (initialized in main.py)
_client: Optional[GnjoyClient] = None
_repo: Optional[ItemRepository] = None


def set_dependencies(client: GnjoyClient, repo: ItemRepository):
    """Set global dependencies for routes."""
    global _client, _repo
    _client = client
    _repo = repo


def get_client() -> GnjoyClient:
    if _client is None:
        raise HTTPException(500, "Crawler client not initialized")
    return _client


def get_repo() -> ItemRepository:
    if _repo is None:
        raise HTTPException(500, "Database not initialized")
    return _repo


# ==================== Server Info ====================

@router.get("/servers")
async def list_servers():
    """Get list of available servers."""
    return {
        "servers": [
            {"id": k, "name": v}
            for k, v in settings.SERVERS.items()
        ]
    }


# ==================== Top Items (On-Demand) ====================

@router.get("/items/top5")
async def get_top5_items(
    category: Optional[str] = Query(None, description="Category: W, D, C, E"),
    force_refresh: bool = Query(False, description="Force refresh from GNJOY"),
    client: GnjoyClient = Depends(get_client),
    repo: ItemRepository = Depends(get_repo),
):
    """
    Get top 5 popular items (on-demand with 5-min cache).

    Categories:
    - W: Weapons
    - D: Defense
    - C: Consumables
    - E: Etc
    """
    cache_key = "top5_all"

    # Check cache first (unless force refresh)
    if not force_refresh:
        cached = await top5_cache.get(cache_key)
        if cached:
            logger.debug("Top5 cache hit")
            if category:
                cat_upper = category.upper()
                cat_data = cached.get(cat_upper, [])
                return {
                    "category": cat_upper,
                    "items": cat_data,
                    "cached": True,
                    "cache_ttl": top5_cache._default_ttl,
                }
            return {**cached, "cached": True}

    # Cache miss or force refresh - crawl from GNJOY
    logger.info("Top5 cache miss - fetching from GNJOY")
    result = await client.fetch_top5_items()

    if result is None:
        # Try to return stale cache or DB data
        cached = await repo.get_cached_top_items()
        if cached:
            return {"items": cached, "stale": True}
        raise HTTPException(503, "Failed to fetch data from GNJOY")

    # Prepare response data (use by_alias=True for GNJOY original field names)
    response_data = {
        "date": result.now_date,
        "W": [i.model_dump(by_alias=True) for i in result.weapons],
        "D": [i.model_dump(by_alias=True) for i in result.defenses],
        "C": [i.model_dump(by_alias=True) for i in result.consumables],
        "E": [i.model_dump(by_alias=True) for i in result.etcs],
    }

    # Store in cache
    await top5_cache.set(cache_key, response_data)

    # Store in DB for history
    await repo.save_top_items(result.weapons, "W")
    await repo.save_top_items(result.defenses, "D")
    await repo.save_top_items(result.consumables, "C")
    await repo.save_top_items(result.etcs, "E")

    # Broadcast to WebSocket subscribers
    await ws_manager.broadcast_all({
        "type": "top5_update",
        "data": response_data,
    })

    if category:
        cat_upper = category.upper()
        return {
            "category": cat_upper,
            "items": response_data.get(cat_upper, []),
            "cached": False,
        }

    return {**response_data, "cached": False}


# ==================== Item Search (On-Demand) ====================

@router.get("/items/search")
async def search_items(
    name: str = Query(..., min_length=1, description="Item name to search"),
    server_id: int = Query(-1, description="Server ID (-1=all)"),
    page: int = Query(1, ge=1, description="Page number"),
    force_refresh: bool = Query(False, description="Force refresh from GNJOY"),
    client: GnjoyClient = Depends(get_client),
    repo: ItemRepository = Depends(get_repo),
) -> ItemSearchResponse:
    """
    Search for item deals by name (on-demand with 1-min cache).

    Server IDs:
    - -1: All servers
    - 1: Baphomet
    - 2: Yggdrasil
    - 3: Dark Lord
    - 4: Ifrit

    Features:
    - Results cached for 60 seconds
    - WebSocket subscribers notified on new data
    - Price history saved automatically
    """
    cache_key = f"search:{name.lower()}:{server_id}:{page}"

    # Check cache first
    if not force_refresh:
        cached = await item_cache.get(cache_key)
        if cached:
            logger.debug(f"Search cache hit: {name}")
            return ItemSearchResponse(
                items=cached["items"],
                total_count=cached["total_count"],
                page=page,
                has_more=cached["has_more"],
            )

    # Cache miss - crawl from GNJOY
    logger.info(f"Search cache miss - fetching '{name}' from GNJOY")
    items = await client.search_item_deals(name, server_id, page)

    # Prepare response
    response_data = {
        "items": items,
        "total_count": len(items),
        "has_more": len(items) >= 20,
    }

    # Store in cache
    await item_cache.set(cache_key, response_data)

    # Save to DB for price history
    if items:
        await repo.save_deal_items(items)

        # Broadcast to subscribers
        await ws_manager.broadcast_item_update(
            item_name=name,
            server_id=server_id,
            data={
                "items": [i.model_dump(by_alias=True) for i in items],
                "count": len(items),
            }
        )

    return ItemSearchResponse(
        items=items,
        total_count=len(items),
        page=page,
        has_more=len(items) >= 20,
    )


# ==================== Price History ====================

@router.get("/items/history")
async def get_price_history(
    name: str = Query(..., description="Item name"),
    server_id: Optional[int] = Query(None, description="Server ID"),
    limit: int = Query(100, le=500, description="Max records"),
    repo: ItemRepository = Depends(get_repo),
):
    """Get price history for an item from database."""
    history = await repo.get_price_history(name, server_id, limit)

    if not history:
        return {"message": "No history found", "items": []}

    return {
        "item_name": name,
        "count": len(history),
        "items": [h.model_dump(by_alias=True) for h in history],
    }


@router.get("/items/average")
async def get_average_price(
    name: str = Query(..., description="Item name"),
    server_id: Optional[int] = Query(None, description="Server ID"),
    days: int = Query(7, ge=1, le=30, description="Days to average"),
    repo: ItemRepository = Depends(get_repo),
):
    """Get average price for an item over specified days."""
    avg = await repo.get_average_price(name, server_id, days)

    if avg is None:
        raise HTTPException(404, f"No price data found for '{name}'")

    return {
        "item_name": name,
        "server_id": server_id,
        "days": days,
        "average_price": round(avg),
    }


# ==================== Cache Statistics ====================

@router.get("/cache/stats")
async def get_cache_stats():
    """Get cache statistics."""
    return {
        "item_cache": item_cache.stats,
        "top5_cache": top5_cache.stats,
    }


@router.post("/cache/clear")
async def clear_cache():
    """Clear all caches (admin operation)."""
    item_cleared = await item_cache.clear()
    top5_cleared = await top5_cache.clear()

    return {
        "item_cache_cleared": item_cleared,
        "top5_cache_cleared": top5_cleared,
    }


# ==================== Statistics ====================

@router.get("/stats")
async def get_stats(repo: ItemRepository = Depends(get_repo)):
    """Get database and system statistics."""
    db_stats = await repo.get_stats()

    return {
        "database": db_stats,
        "cache": {
            "item_cache": item_cache.stats,
            "top5_cache": top5_cache.stats,
        },
        "websocket": ws_manager.stats,
    }


# ==================== Health Check ====================

@router.get("/health")
async def health_check():
    """Health check endpoint."""
    return {
        "status": "ok",
        "service": "ro-market-crawler",
        "mode": "on-demand",
        "connections": ws_manager.connection_count,
    }
