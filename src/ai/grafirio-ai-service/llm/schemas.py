"""
Planner ve executor'un LLM'den bekledigi yapilar.
Dogrulama pydantic ile kod tarafinda yapilir — provider JSON moduna guvenilmez.
"""
import os
from typing import Literal

from pydantic import BaseModel, Field, field_validator, model_validator

ChartType = Literal['bar', 'line', 'area', 'pie', 'doughnut', 'radar', 'scatter']


def _max_plan_tasks() -> int:
    try:
        return max(1, int(os.getenv('MAX_PLAN_TASKS', '5')))
    except ValueError:
        return 5


def _forecast_enabled() -> bool:
    return os.getenv('FORECAST_ENABLED', 'false').strip().lower() in ('1', 'true', 'yes')


class PlanTask(BaseModel):
    kind: Literal['sql_chart', 'forecast', 'narrative']
    question_fragment: str = Field(min_length=3)
    title: str = ''
    chart_type: ChartType = 'bar'
    horizon: int = Field(default=12, ge=1, le=36)


class Plan(BaseModel):
    tasks: list[PlanTask] = Field(min_length=1)

    @model_validator(mode='after')
    def _normalize(self):
        tasks = self.tasks[:_max_plan_tasks()]

        # FORECAST_ENABLED=false iken forecast gorevleri tarihsel trend grafigine dusurulur
        if not _forecast_enabled():
            for t in tasks:
                if t.kind == 'forecast':
                    t.kind = 'sql_chart'
                    t.chart_type = 'line'

        # En fazla 1 narrative; narrative her zaman sona tasinir
        narratives = [t for t in tasks if t.kind == 'narrative']
        others = [t for t in tasks if t.kind != 'narrative']
        self.tasks = others + narratives[:1]
        return self


class SqlChartSpec(BaseModel):
    sql: str = Field(min_length=10)
    chart_type: ChartType = 'bar'
    title: str = ''
    x_label: str = ''
    y_label: str = 'Değer'

    @field_validator('sql')
    @classmethod
    def _select_only(cls, v: str) -> str:
        stripped = v.strip().rstrip(';').strip()
        upper = stripped.upper()
        if not (upper.startswith('SELECT') or upper.startswith('WITH')):
            raise ValueError('Yalnizca SELECT/WITH sorgusu uretilmeli')
        return stripped


class RelevantTables(BaseModel):
    """Sema baglamasi: yuzlerce tablo arasindan soruyla ilgili olanlarin secimi."""
    tables: list[str] = Field(default_factory=list, max_length=12)
