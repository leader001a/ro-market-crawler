# -*- coding: utf-8 -*-
"""HTML Parser for GNJOY item deal pages."""
import re
import logging
from datetime import datetime
from typing import Optional
from bs4 import BeautifulSoup

from src.models.item import DealItem
from src.config import settings

logger = logging.getLogger(__name__)


class ItemDealParser:
    """Parser for GNJOY itemDealList.asp HTML response."""

    # Server name mapping
    SERVER_NAMES = settings.SERVERS

    def parse_deal_list(self, html: str, default_server_id: int = -1) -> list[DealItem]:
        """
        Parse HTML response from itemDealList.asp.

        Args:
            html: HTML content (decoded from EUC-KR)
            default_server_id: Default server ID if not found in row

        Returns:
            List of DealItem parsed from the table
        """
        items = []

        try:
            soup = BeautifulSoup(html, "lxml")

            # Find the deal table - GNJOY uses class="listTypeOfDefault dealList"
            table = soup.find("table", class_="dealList")
            if not table:
                # Try alternative selectors
                table = soup.find("table", class_="tbl_deal")
                if not table:
                    table = soup.find("table", {"id": "dealList"})
                    if not table:
                        tables = soup.find_all("table")
                        for t in tables:
                            if t.find("th", string=re.compile("서버|아이템|가격")):
                                table = t
                                break

            if not table:
                logger.warning("Could not find deal table in HTML")
                return items

            # Parse table rows
            rows = table.find_all("tr")

            for row in rows[1:]:  # Skip header row
                cells = row.find_all("td")
                if len(cells) < 5:
                    continue

                try:
                    item = self._parse_row(cells, default_server_id)
                    if item:
                        items.append(item)
                except Exception as e:
                    logger.debug(f"Error parsing row: {e}")
                    continue

        except Exception as e:
            logger.error(f"Error parsing deal list HTML: {e}")

        return items

    def _parse_row(self, cells: list, default_server_id: int) -> Optional[DealItem]:
        """
        Parse a single table row into DealItem.

        Expected columns (GNJOY format):
        0: Server name
        1: Item (with image, onclick containing item_id)
        2: Quantity
        3: Price (with priceLv* class)
        4: Shop name (with buy/sale class)
        """
        try:
            # Server
            server_text = cells[0].get_text(strip=True)
            server_id = self._parse_server(server_text, default_server_id)
            server_name = self.SERVER_NAMES.get(server_id, server_text)

            # Item cell - extract more info
            item_cell = cells[1]
            item_name, refine, card_slots = self._parse_item_name(item_cell)

            # Extract item_id and item_image_url from the item cell
            item_id = None
            item_image_url = None
            grade = None

            # Find onclick with CallItemDealView(server_id, item_id, ...)
            link = item_cell.find("a", onclick=True)
            if link:
                onclick = link.get("onclick", "")
                # Extract item_id from CallItemDealView(129,1213,'...',1)
                id_match = re.search(r"CallItemDealView\(\d+,(\d+),", onclick)
                if id_match:
                    item_id = int(id_match.group(1))

            # Extract image URL
            img = item_cell.find("img")
            if img and img.get("src"):
                item_image_url = img.get("src")

            # Extract grade from item name (e.g., [UNIQUE], [RARE])
            grade_match = re.search(r"\[(UNIQUE|RARE|EPIC|LEGEND|MYTHIC)\]", item_name, re.IGNORECASE)
            if grade_match:
                grade = grade_match.group(1).upper()
                item_name = re.sub(r"\[(UNIQUE|RARE|EPIC|LEGEND|MYTHIC)\]\s*", "", item_name, flags=re.IGNORECASE)

            # Quantity
            quantity_text = cells[2].get_text(strip=True)
            quantity = self._parse_number(quantity_text)

            # Price - also get formatted version
            price_cell = cells[3]
            price_text = price_cell.get_text(strip=True)
            price = self._parse_number(price_text)
            price_formatted = price_text.replace(" ", "").strip()
            if not price_formatted or price_formatted == "0":
                price_formatted = f"{price:,}"

            # Shop name and deal type (buy/sale)
            shop_cell = cells[4]
            shop_name = shop_cell.get_text(strip=True)
            deal_type = None
            shop_class = shop_cell.get("class", [])
            if isinstance(shop_class, list):
                if "buy" in shop_class:
                    deal_type = "buy"
                elif "sale" in shop_class:
                    deal_type = "sale"
            elif isinstance(shop_class, str):
                if "buy" in shop_class:
                    deal_type = "buy"
                elif "sale" in shop_class:
                    deal_type = "sale"

            # Map (optional - not in current GNJOY format but kept for compatibility)
            map_name = None
            if len(cells) > 5:
                map_name = cells[5].get_text(strip=True) or None

            return DealItem(
                server_id=server_id,
                server_name=server_name,
                item_id=item_id,
                item_name=item_name,
                item_image_url=item_image_url,
                refine=refine,
                grade=grade,
                card_slots=card_slots,
                quantity=quantity,
                price=price,
                price_formatted=price_formatted,
                deal_type=deal_type,
                shop_name=shop_name,
                map_name=map_name,
                crawled_at=datetime.now(),
            )

        except Exception as e:
            logger.debug(f"Failed to parse row: {e}")
            return None

    def _parse_server(self, text: str, default: int) -> int:
        """Parse server name to ID."""
        text = text.strip()

        for server_id, name in self.SERVER_NAMES.items():
            if name in text or text in name:
                return server_id

        # Try numeric match
        match = re.search(r"\d+", text)
        if match:
            return int(match.group())

        return default

    def _parse_item_name(self, cell) -> tuple[str, Optional[int], Optional[str]]:
        """
        Parse item name cell to extract name, refine level, and cards.

        Returns:
            (item_name, refine_level, card_slots)
        """
        text = cell.get_text(strip=True)

        # Extract refine level (+1 ~ +20)
        refine = None
        refine_match = re.search(r"\+(\d+)", text)
        if refine_match:
            refine = int(refine_match.group(1))
            text = re.sub(r"\+\d+\s*", "", text)

        # Extract card slots (usually in brackets or parentheses)
        card_slots = None
        card_match = re.search(r"\[([^\]]+)\]|\(([^\)]+)\)", text)
        if card_match:
            card_slots = card_match.group(1) or card_match.group(2)
            text = re.sub(r"\[[^\]]+\]|\([^\)]+\)", "", text)

        item_name = text.strip()

        return item_name, refine, card_slots

    def _parse_number(self, text: str) -> int:
        """Parse number string, removing commas and units."""
        # Remove commas, 'z' (zeny), and other non-numeric chars
        clean = re.sub(r"[^\d]", "", text)
        return int(clean) if clean else 0
