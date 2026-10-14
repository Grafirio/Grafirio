"""Narrow forecast training to complete calendar months, never widen user scope."""
from datetime import date, datetime, timezone, timedelta
from query_spec import ColumnRef, Predicate


def today_utc():
    return datetime.now(timezone.utc).date()


def next_month(value):
    return date(value.year + (value.month == 12), value.month % 12 + 1, 1)


def complete_month_history(period, predicates, bound, reference_date=None):
    reference = reference_date or today_utc()
    lower = None
    upper = reference.replace(day=1)
    excluded = []
    original_lower = None
    original_upper = None
    for predicate in predicates:
        if (predicate.column.alias, predicate.column.name) != (period.alias, period.name):
            continue
        if predicate.op == 'IS NOT NULL':
            continue
        if predicate.op not in ('>=', '>', '<', '<=') or len(predicate.param_keys) != 1:
            raise ValueError("Tahmin için tarih filtresini başlangıç ve bitiş aralığı olarak belirtin; yalnızca tam aylar kullanılabilir.")
        raw = bound[predicate.param_keys[0]]
        try:
            parsed = datetime.fromisoformat(str(raw).replace('Z', '+00:00'))
        except (TypeError, ValueError):
            raise ValueError("Tahmin tarih aralığı çözümlenemedi. Başlangıç ve bitiş tarihlerini belirtin.")
        month_start = parsed.date().replace(day=1)
        if predicate.op in ('>=', '>'):
            rounded = month_start if parsed.day == 1 and parsed.time().isoformat() == '00:00:00' and predicate.op == '>=' else next_month(month_start)
            lower = max(lower, rounded) if lower else rounded
            original_lower = str(raw)
            if rounded != month_start:
                excluded.append(month_start.strftime('%Y-%m'))
        else:
            upper = min(upper, month_start)
            original_upper = str(raw)
            if parsed.date() != month_start or parsed.time().isoformat() != '00:00:00' or predicate.op == '<=':
                excluded.append(month_start.strftime('%Y-%m'))
    if lower and lower >= upper:
        raise ValueError("Seçilen tarih aralığında tamamlanmış ay yok. Daha uzun bir geçmiş seçin.")
    narrowed = list(predicates)
    parameters = dict(bound)
    for operator, value in [('>=', lower), ('<', upper)]:
        if value is None:
            continue
        key = 'fh' + str(len(parameters))
        while key in parameters:
            key += '_'
        parameters[key] = value.isoformat()
        narrowed.append(Predicate(ColumnRef(period.alias, period.name), operator, [key]))
    note = "Tahmin geçmişinde yalnızca tamamlanmış takvim ayları kullanıldı; içinde bulunulan ay dahil edilmedi."
    if excluded:
        note += " Tarih filtresinde kısmi kalan aylar çıkarıldı: " + ', '.join(sorted(set(excluded))) + '.'
    return narrowed, parameters, {
        'referenceDate': reference.isoformat(), 'fromInclusive': lower.isoformat() if lower else None,
        'toExclusive': upper.isoformat(), 'requestedFrom': original_lower, 'requestedTo': original_upper,
        'excludedPartialMonths': sorted(set(excluded)), 'description': note,
        'lastCompleteMonth': (upper - timedelta(days=1)).strftime('%Y-%m'),
    }
