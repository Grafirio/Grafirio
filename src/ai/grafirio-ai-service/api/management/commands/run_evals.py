"""
Golden eval seti: planner'in tutarliligini olcer.

Kullanim:
  docker compose exec django.ai python manage.py run_evals
  docker compose exec django.ai python manage.py run_evals --min-pass 0.8 --skip-sql

Her soru icin plan uretilir; gorev sayisi/tipi beklentiyle karsilastirilir.
expect.run_sql=true olan sorularda sql_chart gorevleri icin gercekten SQL
uretilip (DB erisilebilirse) calistirilir — hatasiz + satir>0 beklenir.
"""
import json
import os
import time

from django.core.management.base import BaseCommand

from llm.client import LLMClient, LLMError
from llm.metrics import metrics_prompt_block
from tasks.ai_tasks import DB_SCHEMA as BASE_SCHEMA
from tasks.executor import SQL_GEN_SYSTEM, _generate_sql_spec
from tasks.planner import build_plan

EVALS_PATH = os.path.join(os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))), 'evals', 'questions.jsonl')


class Command(BaseCommand):
    help = 'Planner golden eval setini calistirir ve PASS/FAIL orani raporlar.'

    def add_arguments(self, parser):
        parser.add_argument('--min-pass', type=float, default=0.8,
                            help='Bu oranin altinda exit code 1 doner (varsayilan 0.8)')
        parser.add_argument('--skip-sql', action='store_true',
                            help='expect.run_sql=true olsa da SQL calistirmayi atla (DB gerekmez)')
        parser.add_argument('--path', type=str, default=EVALS_PATH,
                            help='questions.jsonl yolu')

    def handle(self, *args, **options):
        path = options['path']
        min_pass = options['min_pass']
        skip_sql = options['skip_sql']

        try:
            with open(path, encoding='utf-8') as f:
                cases = [json.loads(line) for line in f if line.strip()]
        except FileNotFoundError:
            self.stderr.write(self.style.ERROR(f'Eval dosyasi bulunamadi: {path}'))
            raise SystemExit(1)

        llm = LLMClient()
        db_schema = BASE_SCHEMA + metrics_prompt_block()

        passed, failed = 0, 0
        for i, case in enumerate(cases, 1):
            question = case['question']
            expect = case.get('expect', {})
            ok, reason = self._run_case(llm, db_schema, question, expect, skip_sql)
            if ok:
                passed += 1
                self.stdout.write(self.style.SUCCESS(f'[{i}/{len(cases)}] PASS  {question[:70]}'))
            else:
                failed += 1
                self.stdout.write(self.style.ERROR(f'[{i}/{len(cases)}] FAIL  {question[:70]}  — {reason}'))
            time.sleep(0.5)  # LLM rate-limit'e nazik davran

        total = passed + failed
        rate = passed / total if total else 0.0
        self.stdout.write('')
        self.stdout.write(self.style.WARNING(
            f'Sonuc: {passed}/{total} PASS (%{rate * 100:.1f})'))

        if rate < min_pass:
            self.stderr.write(self.style.ERROR(
                f'Basari orani esigin altinda: %{rate * 100:.1f} < %{min_pass * 100:.1f}'))
            raise SystemExit(1)

    def _run_case(self, llm, db_schema, question, expect, skip_sql):
        try:
            plan = build_plan(question, [], llm, db_schema)
        except LLMError as e:
            return False, f'Planner hatasi: {str(e)[:150]}'

        n = len(plan.tasks)
        min_t, max_t = expect.get('min_tasks', 1), expect.get('max_tasks', 5)
        if not (min_t <= n <= max_t):
            return False, f'Gorev sayisi beklenmedik: {n} (beklenen {min_t}-{max_t})'

        expected_kinds = set(expect.get('kinds', []))
        actual_kinds = {t.kind for t in plan.tasks}
        if expected_kinds and not actual_kinds.issubset(expected_kinds):
            return False, f'Beklenmeyen gorev tipi: {actual_kinds} (beklenen alt kume: {expected_kinds})'

        if expect.get('run_sql') and not skip_sql:
            for task in plan.tasks:
                if task.kind != 'sql_chart':
                    continue
                try:
                    spec = _generate_sql_spec(llm, task, db_schema, SQL_GEN_SYSTEM)
                except Exception as e:
                    return False, f'SQL uretilemedi ({task.title}): {str(e)[:150]}'
                try:
                    from tasks.ai_tasks import execute_sql
                    _, rows = execute_sql(spec.sql)
                except Exception as e:
                    return False, f'SQL calistirilamadi ({task.title}): {str(e)[:150]}'
                if not rows:
                    return False, f'SQL bos sonuc dondu ({task.title})'

        return True, ''
