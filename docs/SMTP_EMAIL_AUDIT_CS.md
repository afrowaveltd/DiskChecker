# SMTP a e-mailový certifikát — audit

- SMTP v Avalonia: host, port, TLS, uživatel, heslo, odesílatel, příjemce, PDF příloha, testovací odeslání.
- Zapnuté TLS: port 465 používá implicitní SSL, ostatní porty povinný STARTTLS. Bez validního certifikátu nebo STARTTLS spojení selže; žádný tichý fallback na plaintext.
- Vypnuté TLS: vědomě nešifrované SMTP pro důvěryhodný intranet. Heslo se může přenášet nešifrovaně.
- Povrchový read-only test s nastaveným příjemcem generuje po dokončení PDF certifikátu a odešle ho podle nastavení příloh. Případná chyba SMTP se ukáže v UI; test zůstane dokončen.
- Automatický e-mail pro Safe/Absolute destructive testy zatím není připojen k procesu certifikace.
- Skutečné doručení ověřit proti vlastnímu SMTP serveru; CI nemá přístup k externí schránce.
