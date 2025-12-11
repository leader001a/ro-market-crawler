# -*- coding: utf-8 -*-
"""GNJOY API Client for crawling item deal data."""
import asyncio
import logging
from typing import Optional
import httpx

from src.config import settings
from src.models.item import (
    Top5Response,
    TopItem,
    DealItem,
    ItemCategory,
)
from src.crawler.parser import ItemDealParser

logger = logging.getLogger(__name__)


class GnjoyClient:
    """Async HTTP client for GNJOY Ragnarok Online API."""

    def __init__(self):
        self.base_url = settings.GNJOY_BASE_URL
        self.delay = settings.REQUEST_DELAY_SECONDS
        self.parser = ItemDealParser()
        self._client: Optional[httpx.AsyncClient] = None

    async def _get_client(self) -> httpx.AsyncClient:
        """Get or create HTTP client."""
        if self._client is None or self._client.is_closed:
            self._client = httpx.AsyncClient(
                timeout=30.0,
                headers={
                    "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36",
                    "Accept": "application/json, text/html, */*",
                    "Accept-Language": "ko-KR,ko;q=0.9,en-US;q=0.8,en;q=0.7",
                    "Referer": "https://ro.gnjoy.com/",
                },
            )
        return self._client

    async def close(self):
        """Close HTTP client."""
        if self._client and not self._client.is_closed:
            await self._client.aclose()

    async def fetch_top5_items(self) -> Optional[Top5Response]:
        """
        Fetch top 5 popular items from GNJOY.

        Returns:
            Top5Response with items categorized by W/D/C/E
        """
        client = await self._get_client()

        try:
            response = await client.get(settings.TOP5_ENDPOINT)
            response.raise_for_status()

            data = response.json()

            # GNJOY API returns array format:
            # [0]: {ErrorCode, ErrorMessage, NowDate}
            # [1]: {data: [{equipment:"W",...}, {rankNumber:1,...}, ...]}
            # [2]: {data: [{equipment:"D",...}, ...]}
            # [3]: {data: [{equipment:"C",...}, ...]}
            # [4]: {data: [{equipment:"E",...}, ...]}

            if not isinstance(data, list) or len(data) < 5:
                logger.error(f"Unexpected GNJOY API response format")
                return None

            header = data[0]
            if str(header.get("ErrorCode")) != "0":
                logger.error(f"GNJOY API error: {header.get('ErrorMessage')}")
                return None

            now_date = header.get("NowDate", "")

            # Parse each category
            weapons = self._parse_category_items(data[1].get("data", []))
            defenses = self._parse_category_items(data[2].get("data", []))
            consumables = self._parse_category_items(data[3].get("data", []))
            etcs = self._parse_category_items(data[4].get("data", []))

            # Set category for each item
            for item in weapons:
                item.category = ItemCategory.WEAPON
            for item in defenses:
                item.category = ItemCategory.DEFENSE
            for item in consumables:
                item.category = ItemCategory.CONSUMABLE
            for item in etcs:
                item.category = ItemCategory.ETC

            result = Top5Response(
                error_code=0,
                error_message="",
                now_date=now_date,
                weapons=weapons,
                defenses=defenses,
                consumables=consumables,
                etcs=etcs,
            )

            logger.info(f"Fetched top5 items: W={len(weapons)}, D={len(defenses)}, C={len(consumables)}, E={len(etcs)}")
            return result

        except httpx.HTTPStatusError as e:
            logger.error(f"HTTP error fetching top5: {e}")
            return None
        except Exception as e:
            logger.error(f"Error fetching top5: {e}")
            return None

    def _parse_category_items(self, data_list: list) -> list[TopItem]:
        """Parse category items from GNJOY response data array."""
        items = []
        for item_data in data_list:
            # Skip the header item (contains "equipment" key)
            if "equipment" in item_data:
                continue
            try:
                item = TopItem(
                    rankNumber=int(item_data.get("rankNumber", 0)),
                    itemID=int(item_data.get("itemID", 0)),
                    itemName=item_data.get("itemName", ""),
                    itemCnt=int(item_data.get("itemCnt", 0)),
                    rankState=item_data.get("rankState", "-"),
                )
                items.append(item)
            except (ValueError, KeyError) as e:
                logger.debug(f"Skipping invalid item: {e}")
        return items

    async def search_item_deals(
        self,
        item_name: str,
        server_id: int = -1,
        page: int = 1,
    ) -> list[DealItem]:
        """
        Search for item deals by name.

        Args:
            item_name: Item name to search (Korean)
            server_id: Server ID (-1=all, 1=바포, 2=이그, 3=다크, 4=이프)
            page: Page number (1-based)

        Returns:
            List of DealItem found
        """
        client = await self._get_client()

        try:
            # Add delay to avoid rate limiting
            await asyncio.sleep(self.delay)

            params = {
                "svrID": server_id,
                "itemFullName": item_name,
                "itemOrder": "regdate",
                "curpage": page,
            }

            response = await client.get(
                settings.DEAL_LIST_ENDPOINT,
                params=params,
            )
            response.raise_for_status()

            # GNJOY returns UTF-8 encoded HTML (despite legacy comments)
            html_content = response.content.decode("utf-8", errors="replace")

            items = self.parser.parse_deal_list(html_content, server_id)
            logger.info(f"Found {len(items)} deals for '{item_name}' on server {server_id}")

            return items

        except httpx.HTTPStatusError as e:
            logger.error(f"HTTP error searching items: {e}")
            return []
        except Exception as e:
            logger.error(f"Error searching items: {e}")
            return []

    async def fetch_all_top_items(self) -> dict[str, list[TopItem]]:
        """
        Fetch all top items categorized.

        Returns:
            Dict with keys 'weapons', 'defenses', 'consumables', 'etcs'
        """
        result = await self.fetch_top5_items()

        if result is None:
            return {
                "weapons": [],
                "defenses": [],
                "consumables": [],
                "etcs": [],
            }

        return {
            "weapons": result.weapons,
            "defenses": result.defenses,
            "consumables": result.consumables,
            "etcs": result.etcs,
        }
