// =======================================================
// Trade Service (server)
// BUY (jak było) + SELL (nowe RPC) – czysty ASCII
// =======================================================

function Trade_priceForQ(%baseCopperAtQ50,%q){
  if (%q $= "" || %q <= 0) %q = 50;
  %p = mFloor(%baseCopperAtQ50 * (%q / 50.0));
  if (%p < 0) %p = 0;
  return %p;
}

// ------------------------ BUY: payload i RPC ------------------------
function Trade_makeListPayload(){
  %out = "";
  for (%i=0; %i<TradeOffers.getCount(); %i++){
    %r = TradeOffers.getObject(%i);
    %q    = (%r.q > 0 ? %r.q : 50);
    %icon = (%r.iconPath !$= "" ? %r.iconPath : %r.icon);
    %name = (%r.displayName !$= "" ? %r.displayName : %r.name);
    %unit = Trade_priceForQ(%r.unitPrice, %q);
    // id;name;icon;q;unit;dur|
    %out = %out @ %r.itemId @ ";" @ %name @ ";" @ %icon @ ";" @ %q @ ";" @ %unit @ ";" @ %r.durability @ "|";
  }
  return %out;
}

function serverCmdTrade_Request(%client,%ctx){
  %payload = Trade_makeListPayload();
  commandToClient(%client, 'Trade_Open', %payload);
}

function serverCmdTrade_Buy(%client,%ctx,%itemId,%qty){
  %qty = mClamp(mFloor(%qty), 1, 100000);

  %rec = "";
  for (%i=0; %i<TradeOffers.getCount(); %i++){ %r=TradeOffers.getObject(%i); if (%r.itemId $= %itemId){ %rec=%r; break; } }
  if (%rec $= "") { commandToClient(%client,'Trade_Error',"Unknown item"); return; }

  %qItem = (%rec.q > 0 ? %rec.q : 50);
  %unit  = Trade_priceForQ(%rec.unitPrice, %qItem);
  %total = %unit * %qty;

  echo("[Trade] BUY item="@(%rec.displayName !$= "" ? %rec.displayName : %rec.name)@" id="@%rec.itemId@" unit="@%unit@" qty="@%qty@" total="@%total@" Q="@%qItem);

  %tx = new ScriptObject();
  %tx.class  = "TradeBuyCtx";
  %tx.client = %client;
  %tx.rec    = %rec;
  %tx.q      = %qItem;
  %tx.qty    = %qty;
  %tx.total  = %total;

  Trade_Pay_DB(%client, %total, %tx, "Trade_cbPaid");
}

function Trade_cbPaid(%ctx,%ok,%msg){
  %client = %ctx.client;
  %rec    = %ctx.rec;

  if (!%ok){
    commandToClient(%client,'Trade_Error',(%msg $= "" ? "Payment failed" : %msg));
    %ctx.delete();
    return;
  }

  %added = TradeInv_Add(%client,%rec.itemId,%ctx.qty,%ctx.q);

  if (!%added){
    // zostawiamy domyślne zachowanie gry – wyrzuci na ziemię
    commandToClient(%client,'Trade_Info',"No space in inventory — items may drop nearby.");
    %ctx.delete();
    return;
  }

  commandToClient(%client,'Trade_Info',"Bought: "@%ctx.qty@"x " @ (%rec.displayName !$= "" ? %rec.displayName : %rec.name) @ " (Q" @ %ctx.q @ ")");
  %ctx.delete();
}

// ------------------------ SELL: payload i RPC ------------------------
function Trade_makeSellPayload(){
  %out = "";
  for (%i=0; %i<TradeSell.getCount(); %i++){
    %r = TradeSell.getObject(%i);
    %icon = (%r.iconPath !$= "" ? %r.iconPath : %r.icon);
    %name = (%r.displayName !$= "" ? %r.displayName : %r.name);
    // id;name;icon;minQ;unitPrice|
    %out = %out @ %r.itemId @ ";" @ %name @ ";" @ %icon @ ";" @ %r.minQ @ ";" @ %r.unitPrice @ "|";
  }
  return %out;
}

function serverCmdTrade_RequestSell(%client,%ctx){
  %payload = Trade_makeSellPayload();
  commandToClient(%client,'Trade_OpenSell',%payload);
}

// ======================= SELL SERVICE (server buys from player) =======================

function Trade_findSell(%itemId)
{
   for (%i=0; %i<TradeSell.getCount(); %i++)
   {
      %r = TradeSell.getObject(%i);
      if (%r.itemId $= %itemId) return %r;
   }
   return "";
}

// GUI powinno wysyłać: serverCmdTrade_Sell(%ctx, %itemId, %qty)
// %qty – ile gracz chce sprzedać
function serverCmdTrade_Sell(%client, %ctx, %itemId, %qty)
{
   %qty = mClamp(mFloor(%qty), 1, 1000000);

   %rec = Trade_findSell(%itemId);
   if (%rec $= "")
   {
      commandToClient(%client, 'Trade_Error', "Nothing to buy for that item.");
      return;
   }

   %minQ = (%rec.minQ > 0 ? %rec.minQ : 1);
   %unit = mClamp(mFloor(%rec.unitPrice), 1, 2147483647);

   echo("[Trade] SELL ask item=" @ (%rec.displayName !$= "" ? %rec.displayName : %rec.name) @
        " id=" @ %rec.itemId @ " minQ=" @ %minQ @ " unit=" @ %unit @ " qty=" @ %qty);

   %tx = new ScriptObject();
   %tx.class   = "TradeSellCtx";
   %tx.client  = %client;
   %tx.rec     = %rec;
   %tx.minQ    = %minQ;
   %tx.askQty  = %qty;
   %tx.unit    = %unit;

   Trade_Sell_DB(%client, %rec.itemId, %minQ, %qty, %unit, %tx, "Trade_cbSold");
}

function Trade_cbSold(%ctx, %ok, %msg)
{
   %client = %ctx.client;
   %rec    = %ctx.rec;

   if (!%ok)
   {
      commandToClient(%client, 'Trade_Error', %msg);
      %ctx.delete();
      return;
   }

   // wypłata
   if (%ctx.payoutC > 0)
      Trade_PayOut(%client, %ctx.payoutC);

   %sold = (%ctx.soldQty > 0 ? %ctx.soldQty : 0);
   if (%sold <= 0)
   {
      commandToClient(%client, 'Trade_Error', "Nothing sold.");
      %ctx.delete();
      return;
   }

   commandToClient(%client, 'Trade_Info',
      "Sold: " @ %sold @ "x " @ (%rec.displayName !$= "" ? %rec.displayName : %rec.name) @
      " (min Q" @ %ctx.minQ @ ") for " @ %ctx.payoutC @ "c");

   %ctx.delete();
}
