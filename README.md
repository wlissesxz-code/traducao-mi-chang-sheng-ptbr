# Tradução PT-BR — 觅长生 (Amazing Cultivation Simulator / Mi Chang Sheng)

觅长生 汉化 → 巴西葡萄牙语 (PT-BR) 翻译补丁

Tradução não oficial para português do Brasil do jogo **觅长生**, feita como plugin BepInEx. Tudo roda localmente, sem serviços externos.

**Versão atual: 0.40**

## O que está traduzido
- Interface, menus, botões, tooltips e painéis (incluindo imagens com texto, repintadas em português).
- Diálogos, escolhas, missões, cartas e eventos.
- Dados do jogo: técnicas, habilidades, buffs, pílulas, tesouros, feras, itens, locais, cargos e nomes de NPC (em pinyin).
- Textos montados pelo jogo em tempo de execução (log de eventos, leilão, batalha, Dao etc.), por modelos com números.

A tradução é um trabalho em andamento. Ainda existem trechos em chinês e textos que não cabem nas caixas; veja "Reportar problemas" abaixo.

## Instalação
Este plugin é independente: **não precisa do mod de tradução em inglês** (EN Translation). Só precisa do mod de base que carrega plugins BepInEx no jogo, na Oficina Steam: [item 2824349934](https://steamcommunity.com/sharedfiles/filedetails/?id=2824349934) (assine-o e abra o jogo uma vez).

### Instalação automática (Windows, recomendada)
1. Baixe `MCS-PTBR-Menus-v0.40.zip` em **Releases** e **extraia tudo** (não rode de dentro do zip).
2. Feche o jogo e dê dois cliques em **`INSTALAR.bat`**.
3. O instalador procura sozinho a pasta do mod de base nas suas bibliotecas do Steam, copia a tradução e avisa se o mod em inglês estiver assinado. Se não achar a pasta, ele pede o caminho.
4. Se estiver com o mod de tradução em inglês ativo, **desative-o** (ele conflita com este plugin).
5. Abra o jogo. No `BepInEx\LogOutput.log` deve aparecer `PT-BR ativo: ... entradas`.

Para desinstalar, dê dois cliques em **`DESINSTALAR.bat`**.

### Instalação manual
Copie a pasta `MCS-PTBR-Menus` para `...\steamapps\workshop\content\1189490\2824349934\BepInEx\plugins\`. Para desinstalar, apague essa pasta.

## Reportar problemas
Abra uma *issue* e anexe:
- um print da tela com o problema;
- o arquivo `BepInEx\LogOutput.log`;
- se existir, `MCS-PTBR-Menus\pendentes-dados.txt` (lista de textos que ainda estão em chinês).

## Para quem quer compilar
O código-fonte está em `src/MenusPTBR.cs` (C# 5, plugin BepInEx/Harmony para Unity 2018). A pasta `MCS-PTBR-Menus` já inclui a DLL compilada.

Os arquivos `.tsv` são dicionários `texto original<TAB>tradução` (UTF-8):
- `menus.tsv` e `lote-*.tsv`: textos de interface e diálogos.
- `dados-*.tsv`: textos das tabelas de dados do jogo.
- `pinyin.tsv`: pinyin por caractere, usado para gerar nomes de NPC que faltam nos dicionários.
- `Texture\`: imagens repintadas, trocadas pelo nome da textura original.

## Créditos
- Tradução e plugin: wlissesxz.
- `Texture\sum.png` vem do mod de tradução em inglês da comunidade.
- Dados de pinyin: [mozillazg/pinyin-data](https://github.com/mozillazg/pinyin-data) (licença MIT).

## Aviso
Projeto de fãs, sem ligação com os desenvolvedores ou a editora do jogo. As imagens repintadas derivam da arte original do jogo e só devem ser usadas junto com uma cópia legítima dele.

## Licença
[CC BY-NC 4.0](LICENSE): uso e modificação livres, com crédito e sem fins comerciais.
