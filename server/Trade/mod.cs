// =======================================================
// Trade Mod (server) – loader + INI (BUY + SELL)
// Czysty ASCII / UTF-8 bez BOM
// =======================================================

function Trade__stripComment(%s) {
   %p = strPos(%s, ";");
   if (%p >= 0) return trim(getSubStr(%s, 0, %p));
   return trim(%s);
}
function Trade__normSlashes(%s) {
   return strreplace(%s, "\\", "/");
}
function Trade__joinIcon(%root, %rel) {
   %rel = Trade__normSlashes(%rel);
   if (getSubStr(%rel, 0, 1) $= "/")
      %rel = getSubStr(%rel, 1, 999);
   if (%root $= "") return %rel;
   return Trade__normSlashes(%root @ %rel);
}

if (!isObject(TradeOffers)) new SimSet(TradeOffers); // BUY
if (!isObject(TradeSell))   new SimSet(TradeSell);   // SELL

$TradeCfg::Path = "mods/server/trade/trade.ini";

// załaduj subsystemy
exec("mods/server/trade/trade_money.cs");
exec("mods/server/trade/trade_svc.cs");
exec("mods/server/trade/triger_gui.cs");

// ---------- load config ----------
function Trade_reloadConfig() {
   // reset list
   while (TradeOffers.getCount() > 0) TradeOffers.getObject(0).delete();
   while (TradeSell.getCount()   > 0) TradeSell.getObject(0).delete();

   // domyślne wartości
   $TradeCfg::CopperID = 1059;
   $TradeCfg::SilverID = 1060;
   $TradeCfg::GoldID   = 1061;
   $TradeCfg::CPS      = 100;   // copper per silver
   $TradeCfg::SPG      = 100;   // silver per gold
   $TradeCfg::IconRoot = "gui/forms/";
   $TradeCfg::RefundOnFail = 0; // zostawiamy drop na ziemię gdy brak miejsca

   %f = new FileObject();
   if (!%f.openForRead($TradeCfg::Path)) {
      error("[Trade] Missing INI: " @ $TradeCfg::Path);
      %f.delete();
      echo("[Trade] Loaded offers: 0 (buy), 0 (sell)");
      return;
   }

   %sec = "";
   %cntBuy = 0;
   %cntSell = 0;

   while (!%f.isEOF()) {
      %ln = Trade__stripComment(%f.readLine());
      if (%ln $= "") continue;

      // sekcja
      if (getSubStr(%ln, 0, 1) $= "[" && getSubStr(%ln, strLen(%ln)-1, 1) $= "]") {
         %sec = strlwr(getSubStr(%ln, 1, strLen(%ln)-2));
         continue;
      }

      // [shop] key=value
      if (%sec $= "shop") {
         %eq = strPos(%ln, "=");
         if (%eq < 0) continue;
         %k = strlwr(trim(getSubStr(%ln, 0, %eq)));
         %v = Trade__normSlashes(trim(getSubStr(%ln, %eq+1, 2048)));
         if (%k $= "copperid")        $TradeCfg::CopperID = %v;
         else if (%k $= "silverid")   $TradeCfg::SilverID = %v;
         else if (%k $= "goldid")     $TradeCfg::GoldID   = %v;
         else if (%k $= "copperpersilver" || %k $= "cps") $TradeCfg::CPS = mFloor(%v);
         else if (%k $= "silverpergold"   || %k $= "spg") $TradeCfg::SPG = mFloor(%v);
         else if (%k $= "iconroot")   $TradeCfg::IconRoot = %v;
         else if (%k $= "refundonfail") $TradeCfg::RefundOnFail = mFloor(%v);
         continue;
      }

      // [offers]  IconRelPath,ID,Name,Quality,PriceCopper[,Durability]
      if (%sec $= "offers") {
         %fld = strreplace(%ln, ",", "\t");
         %icon  = getField(%fld, 0);
         %id    = getField(%fld, 1);
         %name  = getField(%fld, 2);
         %q     = mFloor(getField(%fld, 3));
         %price = mFloor(getField(%fld, 4));
         %dur   = getField(%fld, 5);
         if (%id $= "" || %price <= 0) continue;

         %rec = new ScriptObject();
         %rec.class       = "TradeOffer";
         %rec.itemId      = %id;
         %rec.displayName = %name;
         %rec.iconPath    = Trade__joinIcon($TradeCfg::IconRoot, %icon);
         %rec.q           = (%q > 0 ? %q : 50);
         %rec.unitPrice   = %price;    // cena bazowa przy Q50 (skalowana)
         %rec.durability  = %dur;

         TradeOffers.add(%rec);
         %cntBuy++;
         continue;
      }

      // [sell]  IconRelPath,ID,Name,MinQuality,PriceCopper
      if (%sec $= "sell") {
         %fld = strreplace(%ln, ",", "\t");
         %icon = getField(%fld, 0);
         %id   = getField(%fld, 1);
         %name = getField(%fld, 2);
         %minQ = mFloor(getField(%fld, 3));
         %pc   = mFloor(getField(%fld, 4)); // cena za 1 szt
         if (%id $= "" || %pc <= 0) continue;

         %rec = new ScriptObject();
         %rec.class       = "TradeSellRec";
         %rec.itemId      = %id;
         %rec.displayName = %name;
         %rec.iconPath    = Trade__joinIcon($TradeCfg::IconRoot, %icon);
         %rec.minQ        = (%minQ > 0 ? %minQ : 1);
         %rec.unitPrice   = %pc;

         TradeSell.add(%rec);
         %cntSell++;
         continue;
      }
   }

   %f.close(); %f.delete();
   echo("[Trade] Loaded offers: " @ %cntBuy @ " (buy), " @ %cntSell @ " (sell)");
}

Trade_reloadConfig();

// ======================= SELL CONFIG =======================
// Sekcja w trade.ini:
// [sell]
// iconRelPath,ID,Name,MinQuality,PriceCopperPerUnit
// Przykład:
// icon/iron_ore.png,328,Iron Ore,50,3

if (!isObject(TradeSell)) new SimSet(TradeSell);

function Trade_reloadSell()
{
   while (TradeSell.getCount() > 0) TradeSell.getObject(0).delete();

   %ini = "mods/server/trade/trade.ini";
   if (!isFile(%ini)) { echo("[Trade] SELL ini not found: " @ %ini); return; }

   %f = new FileObject();
   if (!%f.openForRead(%ini)) { %f.delete(); return; }

   %inSell = false;
   %cnt = 0;

   while (!%f.isEOF())
   {
      %L = trim(%f.readLine());
      if (%L $= "" || getSubStr(%L,0,1) $= ";") continue;

      if (getSubStr(%L,0,1) $= "[")
      {
         %sec = strlwr(strreplace(%L,"[",""));
         %sec = strreplace(%sec,"]","");
         %inSell = (%sec $= "sell");
         continue;
      }
      if (!%inSell) continue;

      // icon, id, name, minQ, priceCopper
      %L = strreplace(%L, "\\", "/");
      %icon = trim(getField(strreplace(%L,",","\t"),0));
      %id   = mFloor(trim(getField(strreplace(%L,",","\t"),1)));
      %name = trim(getField(strreplace(%L,",","\t"),2));
      %minQ = mFloor(trim(getField(strreplace(%L,",","\t"),3)));
      %price= mFloor(trim(getField(strreplace(%L,",","\t"),4)));

      if (%id <= 0 || %price <= 0) continue;
      if (%minQ <= 0) %minQ = 1;

      %rec = new ScriptObject() {
         class       = "TradeSellRec";
         itemId      = %id;
         displayName = %name;
         iconPath    = ($TradeCfg::IconRoot !$= "" ? $TradeCfg::IconRoot @ %icon : %icon);
         minQ        = %minQ;
         unitPrice   = %price; // copper per piece
      };
      TradeSell.add(%rec);
      %cnt++;
   }
   %f.close(); %f.delete();
   echo("[Trade] Loaded SELL offers: " @ %cnt);
}

// Załaduj SELL przy starcie modu
Trade_reloadSell();
