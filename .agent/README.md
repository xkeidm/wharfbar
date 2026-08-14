# `.agent/` — AI için kanonik bağlam

Bu klasör **WharfBar ajanları için kaynak gerçektir.** Cursor `.cursor/rules` yalnızca buraya ve `docs/`’a işaret eder.

## Nasıl kullan

1. Her görevde önce [`project.md`](project.md) ve [`boundaries.md`](boundaries.md) oku.
2. Kod/stil için [`conventions.md`](conventions.md).
3. UI / kullanıcı akışları: [`screens/README.md`](screens/README.md) — ekran başına
   ayrı sayfalar (top bar, dock, ayarlar).
4. Stack / yol haritası: [`context/stack.md`](context/stack.md), [`context/roadmap.md`](context/roadmap.md).
5. Teknik ayrıntı için kök [`docs/`](../docs/) — özellikle Win32 ve tuzaklar.
6. Kök indeks: [`../AGENTS.md`](../AGENTS.md).

## Ne yazılmaz buraya

- Uzun Win32 kopyaları → `docs/02-win32-appbar-dwm.md`
- İnsan odaklı pazarlama → `README.md`
- İnce IDE kuralları → `.cursor/rules/` (şişirme)

## Güncelleme kuralı

Davranış, sınır veya mimari değişince ilgili `.agent/` + `docs/` dosyasını **aynı PR/değişiklikte** güncelle.
