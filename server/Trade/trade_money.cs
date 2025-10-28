// ===================== COIN MATH =====================
function Trade_CPS(){ return ($TradeCfg::CPS > 0 ? $TradeCfg::CPS : 100); }
function Trade_SPG(){ return ($TradeCfg::SPG > 0 ? $TradeCfg::SPG : 100); }
function Trade_CSG(){ return Trade_CPS() * Trade_SPG(); }
function Trade_toCopper(%g,%s,%c){ return (%g*Trade_CSG()) + (%s*Trade_CPS()) + %c; }
function Trade_fromCopper(%cp){
  %g = mFloor(%cp / Trade_CSG()); %cp -= %g * Trade_CSG();
  %s = mFloor(%cp / Trade_CPS()); %cp -= %s * Trade_CPS();
  return %g TAB %s TAB %cp;
}

// ===================== INVENTORY API (silnik) =====================
function TradeInv_Add(%client,%typeId,%amount,%quality){
  %pl = isObject(%client.player) ? %client.player : %client.getControlObject();
  if (!isObject(%pl)) { echo("[Trade$] No player on add"); return false; }
  %q = (%quality > 0 ? %quality : 50);
  %ok = %pl.inventoryAddItem(%typeId, %amount, %q, 0, 0);
  return (%ok $= "" ? true : %ok);
}
function TradeInv_RemoveItemId(%client,%itemId){
  %pl = isObject(%client.player) ? %client.player : %client.getControlObject();
  if (!isObject(%pl)) { echo("[Trade$] No player on remove"); return false; }
  return %pl.inventoryRemoveItem(%itemId); // cały stack po ID
}

// ===================== CHAR ID helper =====================
function Trade__getCharId(%client){
  if (isMethod(%client,"getCharacterId")) { %cid = %client.getCharacterId(); if (%cid > 0) return %cid; }
  if (%client.characterId !$= "" && %client.characterId > 0) return %client.characterId;
  if (%client.CharID       !$= "" && %client.CharID > 0)     return %client.CharID;
  %pl = isObject(%client.player) ? %client.player : %client.getControlObject();
  if (isObject(%pl) && isMethod(%pl,"getCharacterId")) { %cid = %pl.getCharacterId(); if (%cid > 0) return %cid; }
  return 0;
}

// ===================== DBI DRIVER (tylko SELECT) =====================
// Jeden globalny obiekt bez 'class'
if (!isObject(TradeSvcDB)) new ScriptObject(TradeSvcDB);

// Per-client: TradeSvcDB.ctx[cid] = %ctx
// %ctx: { clientId, charId, payAmount, cb, root, ... }

function Trade_Pay_DB(%client,%amountCopper,%ctx,%cb){
  if (%amountCopper <= 0) { call(%cb,%ctx,true,"OK"); return; }

  %charId = Trade__getCharId(%client);
  if (%charId <= 0) { echo("[Trade$] No CharID for client "@%client); call(%cb,%ctx,false,"No CharID"); return; }

  %ctx.payAmount = %amountCopper;
  %ctx.charId    = %charId;
  %ctx.cb        = %cb;
  %ctx.clientId  = %client.getId();

  TradeSvcDB.ctx[%ctx.clientId] = %ctx;

  %sql = "SELECT RootContainerID, " @ %ctx.clientId @ " AS Cid FROM `character` WHERE ID=" @ %charId @ " LIMIT 1";
  echo("[Trade$] DBI Select Root: " @ %sql);
  dbi.Select(TradeSvcDB, "onRoot", %sql);
}

function TradeSvcDB::onRoot(%this,%rs){
  if (!isObject(%rs) || !(%rs.ok() && %rs.nextRecord())) { if (isObject(%rs)){ dbi.remove(%rs); %rs.delete(); } return; }

  %root = %rs.getFieldValue("RootContainerID");
  %cid  = %rs.getFieldValue("Cid");
  dbi.remove(%rs); %rs.delete();

  %ctx = %this.ctx[%cid];
  if (!isObject(%ctx)) return;

  if (%root $= "") { %cb=%ctx.cb; call(%cb,%ctx,false,"No RootContainer"); %this.ctx[%cid]=""; return; }

  %ctx.root = %root;

  %C=$TradeCfg::CopperID; %S=$TradeCfg::SilverID; %G=$TradeCfg::GoldID;
  %sql = "SELECT ID,ObjectTypeID,Quantity," @ %cid @ " AS Cid " @
         "FROM `items` WHERE ContainerID=" @ %root @ " AND ObjectTypeID IN (" @ %C @ "," @ %S @ "," @ %G @ ") " @
         "ORDER BY ObjectTypeID ASC, Quantity DESC";
  echo("[Trade$] DBI Select Coins: " @ %sql);
  dbi.Select(%this, "onCoins", %sql);

  %this.lastCid = %cid;
}

function TradeSvcDB::onCoins(%this,%rs){
  %cid = %this.lastCid;
  %ctx = %this.ctx[%cid];
  if (!isObject(%ctx)) { if (isObject(%rs)){ dbi.remove(%rs); %rs.delete(); } return; }

  %C=$TradeCfg::CopperID; %S=$TradeCfg::SilverID; %G=$TradeCfg::GoldID;
  %CPS=Trade_CPS(); %CSG=Trade_CSG();

  %stacksC=new SimSet(); %stacksS=new SimSet(); %stacksG=new SimSet();
  %wc=0; %ws=0; %wg=0;

  if (isObject(%rs) && %rs.ok()){
    while (%rs.nextRecord()){
      %id=mFloor(%rs.getFieldValue("ID"));
      %tid=mFloor(%rs.getFieldValue("ObjectTypeID"));
      %qty=mFloor(%rs.getFieldValue("Quantity"));
      if (%qty<=0) continue;
      %ent=new ScriptObject(){ item=%id; type=%tid; qty=%qty; };
      if (%tid==%C) { %stacksC.add(%ent); %wc+=%qty; }
      else if (%tid==%S) { %stacksS.add(%ent); %ws+=%qty; }
      else if (%tid==%G) { %stacksG.add(%ent); %wg+=%qty; }
    }
    dbi.remove(%rs); %rs.delete();
  }

  %total=Trade_toCopper(%wg,%ws,%wc);
  echo("[Trade$] WALLET cid="@%cid@" g="@%wg@" s="@%ws@" c="@%wc@" => "@%total@"c need="@%ctx.payAmount);

  if (%total < %ctx.payAmount){
    %stacksC.delete(); %stacksS.delete(); %stacksG.delete();
    %cb=%ctx.cb; call(%cb,%ctx,false,"Not enough money");
    %this.ctx[%cid]=""; return;
  }

  // plan: C -> S -> G
  %need=%ctx.payAmount;
  %plan=new SimSet();

  for (%i=0; %i<%stacksC.getCount() && %need>0; %i++){
    %st=%stacksC.getObject(%i);
    if (%st.qty <= %need) { %plan.add(new ScriptObject(){item=%st.item;type=%C;take=%st.qty;mode="full";origQty=%st.qty;}); %need -= %st.qty; }
    else { %plan.add(new ScriptObject(){item=%st.item;type=%C;take=%need;mode="partial";origQty=%st.qty;}); %need = 0; }
  }
  for (%i=0; %i<%stacksS.getCount() && %need>0; %i++){
    %st=%stacksS.getObject(%i); %val=%st.qty*%CPS;
    if (%val <= %need) { %plan.add(new ScriptObject(){item=%st.item;type=%S;take=%st.qty;mode="full";origQty=%st.qty;}); %need -= %val; }
    else { %needS=mCeil(%need/%CPS); %plan.add(new ScriptObject(){item=%st.item;type=%S;take=%needS;mode="partial";origQty=%st.qty;}); %need=0; }
  }
  for (%i=0; %i<%stacksG.getCount() && %need>0; %i++){
    %st=%stacksG.getObject(%i); %val=%st.qty*%CSG;
    if (%val <= %need) { %plan.add(new ScriptObject(){item=%st.item;type=%G;take=%st.qty;mode="full";origQty=%st.qty;}); %need -= %val; }
    else { %needG=mCeil(%need/%CSG); %plan.add(new ScriptObject(){item=%st.item;type=%G;take=%needG;mode="partial";origQty=%st.qty;}); %need=0; }
  }

  %paid=0;
  for (%i=0; %i<%plan.getCount(); %i++){
    %p=%plan.getObject(%i);
    if (%p.type==%C) %paid += %p.take;
    else if (%p.type==%S) %paid += %p.take*%CPS;
    else if (%p.type==%G) %paid += %p.take*%CSG;
  }
  %change=%paid-%ctx.payAmount;
  echo("[Trade$] PLAN cid="@%cid@" paid="@%paid@" change="@%change@"c");

  // apply: remove stacks, potem wydaj resztę
  %client=0; for (%i=0; %i<ClientGroup.getCount(); %i++){ %c=ClientGroup.getObject(%i); if (%c.getId()==%cid){ %client=%c; break; } }
  if (!isObject(%client)){
    %stacksC.delete(); %stacksS.delete(); %stacksG.delete(); %plan.delete();
    %cb=%ctx.cb; call(%cb,%ctx,false,"Client gone"); %this.ctx[%cid]=""; return;
  }

  for (%i=0; %i<%plan.getCount(); %i++){
    %p=%plan.getObject(%i);
    TradeInv_RemoveItemId(%client,%p.item);
    if (%p.mode $= "partial") {
      %rest=%p.origQty-%p.take;
      if (%rest>0) TradeInv_Add(%client,%p.type,%rest,50);
    }
  }

  if (%change>0){
    %parts=Trade_fromCopper(%change);
    %chgG=getField(%parts,0); %chgS=getField(%parts,1); %chgC=getField(%parts,2);
    if (%chgG>0) TradeInv_Add(%client,$TradeCfg::GoldID,%chgG,50);
    if (%chgS>0) TradeInv_Add(%client,$TradeCfg::SilverID,%chgS,50);
    if (%chgC>0) TradeInv_Add(%client,$TradeCfg::CopperID,%chgC,50);
    echo("[Trade$] CHANGE g="@%chgG@" s="@%chgS@" c="@%chgC);
  }

  %stacksC.delete(); %stacksS.delete(); %stacksG.delete(); %plan.delete();
  %cb=%ctx.cb; call(%cb,%ctx,true,"OK");
  %this.ctx[%cid]="";
}

// ======================= PAY OUT (coins -> eq) =======================
function Trade_PayOut(%client, %copper)
{
   if (%copper <= 0) return;
   %parts = Trade_fromCopper(%copper);
   %g = getField(%parts,0);
   %s = getField(%parts,1);
   %c = getField(%parts,2);
   if (%g > 0) TradeInv_Add(%client, $TradeCfg::GoldID,   %g);
   if (%s > 0) TradeInv_Add(%client, $TradeCfg::SilverID, %s);
   if (%c > 0) TradeInv_Add(%client, $TradeCfg::CopperID, %c);
   echo("[Trade$] PAYOUT g=" @ %g @ " s=" @ %s @ " c=" @ %c @ " (" @ %copper @ "c)");
}

// ======================= SELL DB DRIVER =======================
// Reużywamy globalnego ScriptObject TradeSvcDB (nie ma klasy) – patrz BUY DB flow :contentReference[oaicite:1]{index=1}

// Wywołanie wysokiego poziomu:
// Trade_Sell_DB(%client, %itemType, %minQ, %wantQty, %unitCopper, %ctx, "Trade_cbSold")
function Trade_Sell_DB(%client, %typeId, %minQ, %wantQty, %unitCopper, %ctx, %cb)
{
   %cid = Trade__getCharId(%client);
   if (%cid <= 0) { call(%cb, %ctx, false, "No CharID"); return; }

   %ctx.clientId   = %client.getId();
   %ctx.sellTypeId = %typeId;
   %ctx.sellMinQ   = (%minQ <= 0 ? 1 : %minQ);
   %ctx.sellNeed   = mClamp(mFloor(%wantQty), 1, 1000000);
   %ctx.sellUnit   = mClamp(mFloor(%unitCopper), 1, 2147483647);
   %ctx.cb         = %cb;

   TradeSvcDB.ctx["S" @ %ctx.clientId] = %ctx;

   %sql = "SELECT RootContainerID, " @ %ctx.clientId @ " AS Cid FROM `character` WHERE ID=" @ %cid @ " LIMIT 1";
   echo("[Trade$] SELL DBI Select Root: " @ %sql);
   dbi.Select(TradeSvcDB, "onSellRoot", %sql);
}

// Root -> Items (tylko dany typ, Q >= min)
function TradeSvcDB::onSellRoot(%this, %rs)
{
   if (!(isObject(%rs) && %rs.ok() && %rs.nextRecord())) { if (isObject(%rs)){ dbi.remove(%rs); %rs.delete(); } return; }
   %root = %rs.getFieldValue("RootContainerID");
   %cid  = %rs.getFieldValue("Cid");
   dbi.remove(%rs); %rs.delete();

   %ctx = %this.ctx["S" @ %cid];
   if (!isObject(%ctx)) return;

   if (%root $= "") { call(%ctx.cb, %ctx, false, "No Root"); %this.ctx["S" @ %cid] = ""; return; }

   %this.lastSellCid = %cid;
   %ctx.root = %root;

   %sql = "SELECT ID, Quantity, Quality " @
          "FROM `items` WHERE ContainerID=" @ %root @
          " AND ObjectTypeID=" @ %ctx.sellTypeId @
          " AND Quality>=" @ %ctx.sellMinQ @
          " ORDER BY Quality DESC, Quantity DESC, ID ASC";
   echo("[Trade$] SELL DBI Select Items: " @ %sql);
   dbi.Select(%this, "onSellItems", %sql);
}

function TradeSvcDB::onSellItems(%this, %rs)
{
   %cid = %this.lastSellCid;
   %ctx = %this.ctx["S" @ %cid];
   if (!isObject(%ctx)) { if (isObject(%rs)){ dbi.remove(%rs); %rs.delete(); } return; }

   %plan = new SimSet();
   %left = %ctx.sellNeed;
   %found = 0;

   if (isObject(%rs) && %rs.ok())
   {
      while (%rs.nextRecord() && %left > 0)
      {
         %iid = mFloor(%rs.getFieldValue("ID"));
         %qty = mFloor(%rs.getFieldValue("Quantity"));
         %q   = mFloor(%rs.getFieldValue("Quality"));
         if (%qty <= 0) continue;

         %take = (%qty <= %left ? %qty : %left);
         %plan.add(new ScriptObject(){ item=%iid; take=%take; orig=%qty; q=%q; type=%ctx.sellTypeId; });
         %left -= %take;
         %found += %take;
      }
      dbi.remove(%rs); %rs.delete();
   }

   if (%found <= 0)
   {
      %plan.delete();
      call(%ctx.cb, %ctx, false, "No matching items (Q >= " @ %ctx.sellMinQ @ ")");
      %this.ctx["S" @ %cid] = "";
      return;
   }

   // Realizacja – usuwamy całe stacki i ewentualnie odtwarzamy resztę
   %client = 0;
   for (%i=0; %i<ClientGroup.getCount(); %i++) { %c = ClientGroup.getObject(%i); if (%c.getId()==%cid){ %client=%c; break; } }
   if (!isObject(%client))
   {
      %plan.delete();
      call(%ctx.cb, %ctx, false, "Client gone");
      %this.ctx["S" @ %cid] = "";
      return;
   }

   for (%i=0; %i<%plan.getCount(); %i++)
   {
      %p = %plan.getObject(%i);
      // remove whole stack
      TradeInv_RemoveItemId(%client, %p.item);
      // return remainder if partial
      %rest = %p.orig - %p.take;
      if (%rest > 0)
         TradeInv_Add(%client, %p.type, %rest); // Q przy zachowaniu przez silnik – akceptowalne
   }

   // policz wypłatę
   %ctx.soldQty = 0;
   for (%i=0; %i<%plan.getCount(); %i++) %ctx.soldQty += %plan.getObject(%i).take;
   %ctx.payoutC = %ctx.soldQty * %ctx.sellUnit;

   %plan.delete();

   call(%ctx.cb, %ctx, true, "OK");
   %this.ctx["S" @ %cid] = "";
}
