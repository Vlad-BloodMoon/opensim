# Modules WebRTC BloodMoon pour OpenSimulator

**Version recommandée : OpenSimulator 0.9.3.1 / .NET 8 — release `v0.9.3.1-webrtc.3`.**

[English documentation](README.md) · [Installation française de ces modules](BLOODMOON_INSTALL.fr.md) · [Guide français complet : serveur, DLL et API](https://github.com/Vlad-BloodMoon/BloodMoon-Voice/blob/main/docs/INSTALLATION_FR.md)

Ces modules permettent à OpenSimulator de communiquer avec le **serveur vocal BloodMoon Voice**. Ils offrent la voix de proximité en 3D, les appels privés, la voix des groupes OpenSimulator et, avec une API supplémentaire, les conférences IM écrites et vocales entre plusieurs avatars, même sur différents simulateurs.

## Les trois éléments du système

1. **[BloodMoon Voice v1.0.0](https://github.com/Vlad-BloodMoon/BloodMoon-Voice/releases/tag/v1.0.0)** : le serveur vocal WebRTC.
2. **[OpenSimulator v0.9.3.1-webrtc.3](https://github.com/Vlad-BloodMoon/opensim/releases/tag/v0.9.3.1-webrtc.3)** : les trois paires DLL/PDB de cette release, à copier dans le dossier `bin/` de chaque simulateur compatible. Il s'agit de `WebRtcVoice.dll`, `WebRtcVoiceRegionModule.dll` et `WebRtcVoiceServiceModule.dll`, avec leurs trois fichiers `.pdb`.
3. **[OpenSimulator-AdHocConferences](https://github.com/Vlad-BloodMoon/OpenSimulator-AdHocConferences/blob/main/README.fr.md)** : l'API centrale **PHP/MySQL** qu'il faut installer séparément pour les conférences AdHoc.

**La DLL régionale correcte est déjà dans la release `.3`. Ne pas l'écraser avec l'ancienne DLL indépendante du dépôt AdHoc.** Sauvegarder les fichiers et arrêter chaque simulateur avant le remplacement.

## Où mettre ses propres adresses ?

Dans `bin/config/wolfvoice.ini`, conserver les clés et les réglages nécessaires et adapter :

```ini
[WebRtcVoice]
Enabled = true
WebRtcVoiceServerURI = https://voice.example.org:9443
RpcKeyFile = voice_rpc_key
CredKeyFile = voice_cred_key
ConferenceApiUrl = https://conference.example.org/adhocconf/wolf_api.php
```

**Les deux domaines `example.org` sont fictifs** et doivent être remplacés par les vrais noms de vos services. Le premier est le serveur vocal ; le second est le serveur web qui héberge **votre propre** `wolf_api.php`. L'API BloodMoon de production n'est pas librement accessible.

**L'exemple correspond à une nouvelle installation à deux clés** : `RpcKeyFile` pour la liaison des simulateurs et `CredKeyFile` pour les identifiants vocaux. Si ton serveur utilise déjà **une seule clé avec une liste d'IP autorisées**, conserve cette configuration et n'ajoute pas de clé RPC. Les fichiers de clés doivent exister et rester privés.

Le paramètre `ConferenceApiUrl` est nécessaire **sur chaque simulateur** pour les conférences AdHoc inter-simulateurs ; ces conférences nécessitent aussi l'installation et l'autorisation de l'API. Les appels vocaux ordinaires fonctionnent sans cette API.

**[Suivre toute l'installation étape par étape en français](https://github.com/Vlad-BloodMoon/BloodMoon-Voice/blob/main/docs/INSTALLATION_FR.md)**.

## Compatibilité et remerciements

Cette release est construite pour OpenSimulator 0.9.3.1 / .NET 8 et la version modifiée du serveur WolfVoice par BloodMoon, et non pour n'importe quel serveur Janus ou ancienne DLL WolfVoice. Merci aux auteurs de WolfVoice, à Lone Wolf de WolfGrid, aux développeurs OpenSimulator et aux participants aux tests BloodMoon.
