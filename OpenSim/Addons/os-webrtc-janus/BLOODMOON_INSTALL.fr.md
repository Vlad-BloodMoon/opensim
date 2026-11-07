# BloodMoon WebRTC 0.9.3.1 — installation française

**Version : `v0.9.3.1-webrtc.3` — OpenSimulator 0.9.3.1 / .NET 8.**

⚠️ **Compatible avec le serveur vocal WebRTC modifié de BloodMoon.** Le serveur WolfVoice officiel ne possède pas encore le correctif des axes et l'ensemble de nos modifications pour la voix des groupes et conférences. Ne pas présenter cette archive comme compatible avec n'importe quel serveur WolfVoice ni comme un module Janus autonome.

## Contenu de l'archive

- `WebRtcVoice.dll` et son `.pdb`
- `WebRtcVoiceRegionModule.dll` et son `.pdb` — **la DLL testée et validée pour les conférences texte/voix**
- `WebRtcVoiceServiceModule.dll` et son `.pdb`
- `wolfvoice.ini.example`, `README.md`, `BUILD_COMMIT.txt` et `SHA256SUMS`

Les trois paires de modules sont destinées à être utilisées **ensemble** avec la version de base BloodMoon.

## Installation pour un propriétaire de simulateur autorisé

1. Arrête ton simulateur. Sauvegarde les six fichiers DLL/PDB de la précédente version et ton `wolfvoice.ini`.
2. Télécharge l'archive de la nouvelle [release](https://github.com/Vlad-BloodMoon/opensim/releases), puis extrais-la.
3. Remplace dans `bin/` les **trois paires DLL/PDB**. Ne remplace pas les autres modules d'OpenSimulator et ne supprime pas `WebRtcJanusService.dll` s'il existe.
4. Conserve les valeurs actuelles et privées de `WebRtcVoiceServerURI`, `RpcKeyFile`, `CredKeyFile`. Le fichier `wolfvoice.ini.example` est un **exemple**, ne le copie pas tel quel sur une installation configurée.
5. Vérifie que la section `[WebRtcVoice]` contient :

   ```ini
   ConferenceApiUrl = https://bloodmoonpack.com/adhocconf/wolf_api.php
   ```

6. Redémarre le simulateur. Teste la voix locale, l'appel privé, la voix des groupes, puis une conférence IM écrite à plusieurs et un appel vocal.
7. **Refais cette mise à jour sur chaque simulateur** concerné. Pour un avatar situé sur un autre simulateur, le module régional et l'URL de conférence doivent également être configurés de son côté.

## Fonctionnalités

Les conférences IM sont de vraies conversations écrites **à plusieurs avatars**, pas un chat local ni un groupe OpenSimulator. Les invitations et messages fonctionnent entre deux simulateurs différents. L'appel vocal est **facultatif** : chacun peut raccrocher, rester dans le chat écrit et rejoindre de nouveau l'appel, sans faire quitter les autres.

Le service central `wolf_api.php` gère la liste et l'état des participants. L'audio est assuré par le serveur WolfVoice **BloodMoon**.

## Sécurité et limites

Le simulateur doit être autorisé par BloodMoon. Les clés vocales sont privées et **ne sont jamais fournies dans l'archive**. Ne publie pas les clés, mots de passe, URI de capacités privées ou identifiants de session.

Le mécanisme actuel repose sur une clé partagée et des IP autorisées ; il convient à des simulateurs **de confiance**, pas à une ouverture sans contrôle à des administrateurs externes non fiables.

Les correctifs des axes et de notre voix de groupe font partie de l'infrastructure BloodMoon et **ne sont pas encore intégrés au serveur WolfVoice officiel** à la date de cette publication.

Pour en savoir plus : [dépôt AdHoc](https://github.com/Vlad-BloodMoon/OpenSimulator-AdHocConferences) et [documentation détaillée](https://github.com/Vlad-BloodMoon/OpenSimulator-AdHocConferences/blob/main/docs/INSTALLATION.fr.md).

Merci à **Lone Wolf (WolfGrid)** pour son travail à l'origine de WolfVoice.
