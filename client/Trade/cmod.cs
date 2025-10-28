// ===== mods/client/cmod.cs =====
// Klient: Trade GUI + NickTag HUD (zielony tekst)

deactivatePackage(SPClientModPkg);

if (!isObject(SPClientMod))
   new ScriptObject(SPClientMod) { version = "v3.1.2"; };

package SPClientModPkg
{
   function SPClientMod::setup(%this)
   {
      exec("mods/client/Trade/TradeWindow.gui");
      if (!isFunction("onLoadGuiComplete"))
         eval("function onLoadGuiComplete(){ }");
   }

   function clientCmdOpenTradeGui()
   {
      SPClientMod::openTradeGui();
   }

   function SPClientMod::openTradeGui()
   {
      exec("mods/client/Trade/TradeWindow.gui");

      %candidates = "TradeWindow TradeGui TradeDialog TradeMain CM_TradeWindow CmTradeWindow cmTradeWindow";
      for (%i = 0; %i < getWordCount(%candidates); %i++)
      {
         %name = getWord(%candidates, %i);
         if (isObject(%name) && %name.isMemberOfClass("GuiControl"))
         {
            Canvas.pushDialog(%name);
            return;
         }
      }

      %found = SPClientMod::findTradeGuiInGuiGroup();
      if (isObject(%found))
      {
         Canvas.pushDialog(%found);
         return;
      }
   }

   function SPClientMod::findTradeGuiInGuiGroup()
   {
      if (!isObject(GuiGroup)) return 0;

      %fallback = 0;
      for (%i = 0; %i < GuiGroup.getCount(); %i++)
      {
         %o = GuiGroup.getObject(%i);
         %class = %o.getClassName();
         %name  = %o.getName();

         if (%class $= "GuiControl")
         {
            %lname = strlwr(%name);
            if (strstr(%lname, "trade") != -1 || strstr(%lname, "handel") != -1)
               return %o;

            if (!isObject(%fallback))
               %fallback = %o;
         }
      }
      return %fallback;
   }

   function CM_OpenTradeGui() { SPClientMod::openTradeGui(); }

   function SPClientMod::isTradeDialog(%dlg)
   {
      if (!isObject(%dlg)) return false;
      %n = strlwr(%dlg.getName());
      %c = %dlg.getClassName();
      if (%c !$= "GuiControl") return false;
      return (strstr(%n, "trade") != -1 || strstr(%n, "handel") != -1);
   }

   function Canvas::popDialog(%this, %dlg)
   {
      %isTrade = SPClientMod::isTradeDialog(%dlg);
      %r = Parent::popDialog(%this, %dlg);
      if (%isTrade) commandToServer('TradeGuiClosed');
      return %r;
   }
};
activatePackage(SPClientModPkg);
SPClientMod.setup();


// ===== NickTag HUD (zielony kolor wymuszony lub z RGBA) =====

if (!isObject(_SPNickHUD))
{
   new ScriptObject(_SPNickHUD)
   {
      set = new SimSet();
      tickMs = 100;
      _sch = "";
      gameCtrl = 0;
   };
}

function _SPNick_findGameTSCtrl(%node)
{
   if (!isObject(%node)) return 0;
   %cn = %node.getClassName();
   if (%cn $= "GameTSCtrl") return %node;
   for (%i=0;%i<%node.getCount();%i++)
   {
      %hit = _SPNick_findGameTSCtrl(%node.getObject(%i));
      if (isObject(%hit)) return %hit;
   }
   return 0;
}

function _SPNick_ensureRoot()
{
   if (!isObject(_SPNickHUD.gameCtrl))
   {
      %root = Canvas.getContent();
      _SPNickHUD.gameCtrl = _SPNick_findGameTSCtrl(%root);
   }
   if (_SPNickHUD._sch !$= "") cancel(_SPNickHUD._sch);
   _SPNickHUD._sch = _SPNickHUD.schedule(_SPNickHUD.tickMs, "_tick");
}

function _SPNick_prof(%size)
{
   %n = "SPNickProf_" @ %size;
   if (isObject(%n)) return %n;
   new GuiControlProfile(%n)
   {
      fontType="Arial";
      fontSize=%size;
      justify="center";
      shadowOffset="1 1";
      shadowColor="0 0 0 200";
      opaque="0";
   };
   return %n;
}

function _SPNick_hex3(%rgba)
{
   // oczekuje "r g b a" (0..1). Domyślnie zielony
   %r = getWord(%rgba,0); if (%r $= "") %r = 0;
   %g = getWord(%rgba,1); if (%g $= "") %g = 1;
   %b = getWord(%rgba,2); if (%b $= "") %b = 0;

   %r = mClampF(%r,0,1)*255; %g = mClampF(%g,0,1)*255; %b = mClampF(%b,0,1)*255;
   %r = mFloor(%r+0.5); %g = mFloor(%g+0.5); %b = mFloor(%b+0.5);
   %hx="0123456789ABCDEF";
   %rh=getSubStr(%hx,%r>>4,1)@getSubStr(%hx,%r&15,1);
   %gh=getSubStr(%hx,%g>>4,1)@getSubStr(%hx,%g&15,1);
   %bh=getSubStr(%hx,%b>>4,1)@getSubStr(%hx,%b&15,1);
   return %rh @ %gh @ %bh;
}

package SPNickHUDPkg
{
   function clientCmdNPCNickTag_ShowAt(%key,%pos,%text,%rgba)
   {
      _SPNick_ensureRoot();

      %e = 0;
      for (%i=0;%i<_SPNickHUD.set.getCount();%i++)
      {
         %x=_SPNickHUD.set.getObject(%i);
         if (%x.key $= %key) { %e=%x; break; }
      }
      if (!isObject(%e))
      {
         %e = new ScriptObject(){ key=%key; };
         %e.ctrl = new GuiMLTextCtrl()
         {
            profile = _SPNick_prof(24);
            extent = "400 40";
            visible = "1";
            allowColorChars = "1";
         };
         if (isObject(_SPNickHUD.gameCtrl))
            _SPNickHUD.gameCtrl.add(%e.ctrl);
         _SPNickHUD.set.add(%e);
      }

      %e.pos  = %pos;
      %e.text = (%text $= "" ? "Trade" : %text);

      // ZIELONY na sztywno lub wg RGBA (jeśli przyjdzie) – wynik zawsze zielony domyślnie
      %hex = _SPNick_hex3(%rgba); if (%hex $= "" || %hex $= "000000") %hex = "00FF00";

      %e.ctrl.setText("<just:center><color:" @ %hex @ ">" @ %e.text);
      %e.ctrl.setVisible(1);
   }

   function clientCmdNPCNickTag_UpdateAt(%key,%pos)
   {
      for (%i=0;%i<_SPNickHUD.set.getCount();%i++)
      {
         %e=_SPNickHUD.set.getObject(%i);
         if (%e.key $= %key)
         {
            %e.pos = %pos;
            break;
         }
      }
   }

   function clientCmdNPCNickTag_HideKey(%key)
   {
      for (%i=_SPNickHUD.set.getCount()-1;%i>=0;%i--)
      {
         %e=_SPNickHUD.set.getObject(%i);
         if (%e.key $= %key)
         {
            if (isObject(%e.ctrl)) %e.ctrl.delete();
            %e.delete();
            break;
         }
      }
   }
};
activatePackage(SPNickHUDPkg);

function _SPNickHUD::_tick(%this)
{
   if (!isObject(%this.gameCtrl))
   {
      %root = Canvas.getContent();
      %this.gameCtrl = _SPNick_findGameTSCtrl(%root);
      %this._sch = %this.schedule(%this.tickMs, "_tick");
      return;
   }

   %gw = getWord(%this.gameCtrl.extent,0);
   %gh = getWord(%this.gameCtrl.extent,1);

   for (%i=0;%i<%this.set.getCount();%i++)
   {
      %e=%this.set.getObject(%i);
      if (!isObject(%e.ctrl)) continue;

      %p=%e.pos;
      %scr=%this.gameCtrl.project(%p);
      %sx=getWord(%scr,0); %sy=getWord(%scr,1); %sz=getWord(%scr,2);

      %inFront = (%sz > 0);
      %onScreen = (%sx >= 0 && %sx <= %gw && %sy >= 0 && %sy <= %gh);
      %e.ctrl.setVisible(%inFront && %onScreen);

      %w=getWord(%e.ctrl.extent,0);
      %x=mFloor(%sx - (%w/2));
      %y=mFloor(%sy - 24);
      %e.ctrl.position = %x SPC %y;
   }

   %this._sch = %this.schedule(%this.tickMs, "_tick");
}
