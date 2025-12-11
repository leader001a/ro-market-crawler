# -*- coding: utf-8 -*-
"""Data models for RO Market Crawler."""
from datetime import datetime
from enum import Enum
from typing import Optional
from pydantic import BaseModel, Field


class Server(Enum):
    """RO Server IDs."""
    ALL = -1
    BAPHOMET = 1
    YGGDRASIL = 2
    DARK_LORD = 3
    IFRIT = 4

    @property
    def display_name(self) -> str:
        names = {
            -1: "전체",
            1: "바포메트",
            2: "이그드라실",
            3: "다크로드",
            4: "이프리트",
        }
        return names.get(self.value, "Unknown")


class ItemCategory(Enum):
    """Item categories from GNJOY API."""
    WEAPON = "W"      # 무기
    DEFENSE = "D"     # 방어구
    CONSUMABLE = "C"  # 소비
    ETC = "E"         # 기타

    @property
    def display_name(self) -> str:
        names = {
            "W": "무기",
            "D": "방어구",
            "C": "소비",
            "E": "기타",
        }
        return names.get(self.value, "Unknown")


class TopItem(BaseModel):
    """Top ranked item from GNJOY API."""
    rank_number: int = Field(alias="rankNumber", serialization_alias="rankNumber")
    item_id: int = Field(alias="itemID", serialization_alias="itemID")
    item_name: str = Field(alias="itemName", serialization_alias="itemName")
    item_count: int = Field(alias="itemCnt", serialization_alias="itemCnt")
    rank_state: str = Field(alias="rankState", serialization_alias="rankState")
    category: Optional[ItemCategory] = None

    model_config = {
        "populate_by_name": True,
        "by_alias": True,  # Serialize using GNJOY original field names
    }


class DealItem(BaseModel):
    """Item deal listing from GNJOY search."""
    server_id: int = Field(serialization_alias="serverId")
    server_name: str = Field(serialization_alias="serverName")
    item_id: Optional[int] = Field(default=None, serialization_alias="itemId")
    item_name: str = Field(serialization_alias="itemName")
    display_name: Optional[str] = Field(default=None, serialization_alias="displayName")
    item_image_url: Optional[str] = Field(default=None, serialization_alias="itemImageUrl")
    refine: Optional[int] = None
    grade: Optional[str] = None  # UNIQUE, RARE, etc.
    card_slots: Optional[str] = Field(default=None, serialization_alias="cardSlots")
    quantity: int
    price: int
    price_formatted: Optional[str] = Field(default=None, serialization_alias="priceFormatted")
    deal_type: Optional[str] = Field(default=None, serialization_alias="dealType")  # "buy" or "sale"
    shop_name: str = Field(serialization_alias="shopName")
    map_name: Optional[str] = Field(default=None, serialization_alias="mapName")
    crawled_at: datetime = Field(default_factory=datetime.now, serialization_alias="crawledAt")

    model_config = {
        "populate_by_name": True,
    }

    def model_post_init(self, __context) -> None:
        """Generate computed fields after initialization."""
        # Auto-generate price_formatted if not set
        if self.price_formatted is None and self.price is not None:
            self.price_formatted = f"{self.price:,}"
        # Auto-generate display_name if not set
        if self.display_name is None:
            parts = []
            if self.grade:
                parts.append(f"[{self.grade}]")
            if self.refine is not None and self.refine > 0:
                parts.append(f"+{self.refine}")
            parts.append(self.item_name)
            if self.card_slots:
                parts.append(f"[{self.card_slots}]")
            self.display_name = "".join(parts)


class Top5Response(BaseModel):
    """Response from itemTop5BestView.asp."""
    error_code: int = Field(alias="ErrorCode")
    error_message: str = Field(alias="ErrorMessage")
    now_date: str = Field(alias="NowDate")
    weapons: list[TopItem] = Field(default_factory=list, alias="W")
    defenses: list[TopItem] = Field(default_factory=list, alias="D")
    consumables: list[TopItem] = Field(default_factory=list, alias="C")
    etcs: list[TopItem] = Field(default_factory=list, alias="E")

    class Config:
        populate_by_name = True


class PriceHistory(BaseModel):
    """Price history record for an item."""
    id: Optional[int] = None
    item_id: Optional[int] = Field(default=None, serialization_alias="itemId")
    item_name: str = Field(serialization_alias="itemName")
    server_id: int = Field(serialization_alias="serverId")
    price: int
    quantity: int
    shop_name: str = Field(serialization_alias="shopName")
    recorded_at: datetime = Field(default_factory=datetime.now, serialization_alias="recordedAt")

    model_config = {
        "populate_by_name": True,
    }


class RankHistory(BaseModel):
    """Rank history record for top items."""
    id: Optional[int] = None
    item_id: int
    item_name: str
    category: str
    rank: int
    deal_count: int
    recorded_at: datetime = Field(default_factory=datetime.now)


# API Response Models
class ItemSearchRequest(BaseModel):
    """Request model for item search."""
    item_name: str
    server_id: int = -1
    page: int = 1


class ItemSearchResponse(BaseModel):
    """Response model for item search."""
    items: list[DealItem]
    total_count: int = Field(serialization_alias="totalCount")
    page: int
    has_more: bool = Field(serialization_alias="hasMore")

    model_config = {
        "populate_by_name": True,
        "by_alias": True,
    }


class Top5CategoryResponse(BaseModel):
    """Response model for top 5 items by category."""
    category: str
    category_name: str
    items: list[TopItem]
    updated_at: str
