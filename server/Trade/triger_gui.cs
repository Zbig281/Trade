// ===== mods/server/trade/triger_gui.cs =====
// SPTradeGui + NickTag (TRIGGER @ 10m)

deactivatePackage(SPTradeGui);

if (!isObject(SPTradeGui))
{
   new ScriptObject(SPTradeGui)
   {
      openCooldownMs = 1000;

      _initRetry = 0;
      _maxInitRetry = 60;
      _initInterval = 1000;

      _targetShape = "assel.dts";

      _tickHandle = 0;
      _tickMs = 200;

      tagText  = "Trade";
      tagRGBA  = "0 1 0 1";     // ZIELONY
      tagMinMoveDelta = 0.20;

      npcList = "";
   };
}

package SPTradeGui
{
   function SPTradeGui::setup()
   {
      LiFx::registerCallback($LiFx::hooks::onPostInitCallbacks, scheduleInit, SPTradeGui);
      if (isFunction("onMissionLoaded"))
         LiFx::registerCallback($LiFx::hooks::onMissionLoadedCallbacks, scheduleInit, SPTradeGui);
   }

   function SPTradeGui::version() { return "3.1.0"; }

   function SPTradeGui::scheduleInit()
   {
      cancel(SPTradeGui._initHandle);
      SPTradeGui._initRetry = 0;
      SPTradeGui._initHandle = SPTradeGui.schedule(0, tryInit);
   }

   function SPTradeGui::tryInit()
   {
      cancel(SPTradeGui._initHandle);
      if (!SPTradeGui::isWorldReady()) return SPTradeGui::_retryLater();

      %npcs = SPTradeGui::collectNPCs();
      if (getWordCount(%npcs) == 0) return SPTradeGui::_retryLater();

      SPTradeGui.npcList = %npcs;
      SPTradeGui::startProximityLoop();
   }

   function SPTradeGui::_retryLater()
   {
      if (SPTradeGui._initRetry >= SPTradeGui._maxInitRetry) return;
      SPTradeGui._initRetry++;
      SPTradeGui._initHandle = SPTradeGui.schedule(SPTradeGui._initInterval, tryInit);
   }

   function SPTradeGui::isWorldReady()
   {
      if (!$Server::MissionLoaded && !isObject(MissionGroup) && !isObject(ServerGroup)) return false;
      if (isFunction("physicsIsActive") && !physicsIsActive()) return false;
      if (isFunction("isBulletWorldCreated") && !isBulletWorldCreated()) return false;
      return true;
   }

   function SPTradeGui::collectNPCs(%root)
   {
      if (!isObject(%root))
      {
         if (isObject(cmChildObjectsGroup) && cmChildObjectsGroup.getCount() > 0)
            %root = cmChildObjectsGroup;
         else if (isObject(MissionGroup))
            %root = MissionGroup;
         else
            %root = ServerGroup;
      }
      if (!isObject(%root)) return "";
      return SPTradeGui::_collectNPCsRecurse(%root, SPTradeGui._targetShape);
   }

   function SPTradeGui::_shapePath(%o)
   {
      if (!isObject(%o)) return "";
      %v = %o.shapeName; if (%v !$= "") return %v;
      if (%o.isMethod("getDatablock"))
      {
         %db = %o.getDatablock();
         if (isObject(%db))
         {
            %sf = %db.shapeFile; if (%sf !$= "") return %sf;
            %sn = %db.shapeName; if (%sn !$= "") return %sn;
         }
      }
      return "";
   }

   function SPTradeGui::_collectNPCsRecurse(%set, %shapeNeedle)
   {
      %out = "";
      %needle = strlwr(%shapeNeedle);
      for (%i = 0; %i < %set.getCount(); %i++)
      {
         %o = %set.getObject(%i);
         if (!isObject(%o)) continue;

         if (%o.isMemberOfClass("SimSet") || %o.isMemberOfClass("SimGroup"))
         {
            %out = trim(%out SPC SPTradeGui::_collectNPCsRecurse(%o, %shapeNeedle));
            continue;
         }

         %shape = strlwr(SPTradeGui::_shapePath(%o));
         if (%shape !$= "" && strstr(%shape, %needle) != -1)
            %out = trim(%out SPC %o);
      }
      return %out;
   }

   function SPTradeGui::npcAnchorPos(%o)
   {
      if (!isObject(%o)) return "0 0 0";
      %pos = %o.getPosition();
      if (%o.isMethod("getWorldBox"))
      {
         %bb = %o.getWorldBox();
         if (%bb !$= "" && getWordCount(%bb) == 6)
         {
            %maxZ = getWord(%bb,5);
            %pos = setWord(%pos, 2, %maxZ + 0.6);
         }
      }
      return %pos;
   }

   function SPTradeGui::startProximityLoop()
   {
      cancel(SPTradeGui._tickHandle);
      SPTradeGui._tickHandle = SPTradeGui.schedule(SPTradeGui._tickMs, proximityTick);
   }

   function SPTradeGui::proximityTick()
   {
      cancel(SPTradeGui._tickHandle);

      if (SPTradeGui.npcList $= "" || getWordCount(SPTradeGui.npcList) == 0)
         return SPTradeGui::scheduleInit();

      for (%ci = 0; %ci < ClientGroup.getCount(); %ci++)
      {
         %client = ClientGroup.getObject(%ci);
         if (!isObject(%client)) continue;
         %player = %client.player;
         if (!isObject(%player)) continue;

         %ppos = %player.getPosition();

         %nearest = 1e9; %nearNpc = 0;
         foreach$ (%npc in SPTradeGui.npcList)
         {
            if (!isObject(%npc)) continue;
            %d = vectorDist(%ppos, %npc.getPosition());
            if (%d < %nearest) { %nearest = %d; %nearNpc = %npc; }
         }

         %newZone = 0; if (%nearest <= 5.0001) %newZone = 2; else if (%nearest <= 10.0001) %newZone = 1;
         %oldZone = %client.tradeZoneState; if (%oldZone $= "") %oldZone = 0;

         %prevNpc = %client._tagNpc;
         if ((%oldZone > 0) && isObject(%prevNpc) && %prevNpc != %nearNpc)
         {
            commandToClient(%client, 'NPCNickTag_HideKey', %prevNpc.getId());
            %client._tagLastPos = "";
            %prevNpc = 0;
         }

         if (%newZone != %oldZone)
         {
            if (%oldZone == 0 && %newZone == 1)
            {
               %client.cmSendClientMessage(2475, "<color:00FF00>Trader looks at you with interest...");
               if (isObject(%nearNpc))
               {
                  %p = SPTradeGui::npcAnchorPos(%nearNpc);
                  commandToClient(%client, 'NPCNickTag_ShowAt', %nearNpc.getId(), %p, SPTradeGui.tagText, SPTradeGui.tagRGBA);
                  %client._tagNpc = %nearNpc;
                  %client._tagLastPos = %p;
               }
            }
            else if ((%oldZone == 1 || %oldZone == 0) && %newZone == 2)
            {
               %now = getSimTime();
               if (!%client.tradeMustLeave && (%client.lastTradeGuiOpen $= "" || (%now - %client.lastTradeGuiOpen) > SPTradeGui.openCooldownMs))
               {
                  %client.lastTradeGuiOpen = %now;
                  commandToClient(%client, 'OpenTradeGui');
                  %client.cmSendClientMessage(2475, "<color:00FF00>You begin trading...");
               }
               if (isObject(%nearNpc))
               {
                  %p = SPTradeGui::npcAnchorPos(%nearNpc);
                  commandToClient(%client, 'NPCNickTag_ShowAt', %nearNpc.getId(), %p, SPTradeGui.tagText, SPTradeGui.tagRGBA);
                  %client._tagNpc = %nearNpc;
                  %client._tagLastPos = %p;
               }
            }
            else if (%oldZone == 2 && %newZone == 1)
            {
               %client.tradeMustLeave = false;
               %client.cmSendClientMessage(2475, "<color:FF0000>You stepped away – you can come back to trade again.");
            }
            else if ((%oldZone == 2 || %oldZone == 1) && %newZone == 0)
            {
               %client.tradeMustLeave = false;
               %client.cmSendClientMessage(2475, "<color:FF0000>If you need anything, come back anytime.");
               if (isObject(%client._tagNpc))
                  commandToClient(%client, 'NPCNickTag_HideKey', %client._tagNpc.getId());
               %client._tagNpc = "";
               %client._tagLastPos = "";
            }

            %client.tradeZoneState = %newZone;
         }

         if (%newZone > 0 && isObject(%nearNpc))
         {
            %pNow = SPTradeGui::npcAnchorPos(%nearNpc);
            %pLast = %client._tagLastPos;
            if (%pLast $= "" || VectorDist(%pLast, %pNow) >= SPTradeGui.tagMinMoveDelta)
            {
               commandToClient(%client, 'NPCNickTag_UpdateAt', %nearNpc.getId(), %pNow);
               %client._tagNpc = %nearNpc;
               %client._tagLastPos = %pNow;
            }
         }
      }

      SPTradeGui._tickHandle = SPTradeGui.schedule(SPTradeGui._tickMs, proximityTick);
   }

   function serverCmdTradeGuiClosed(%client)
   {
      if (!isObject(%client)) return;
      %client.tradeMustLeave = true;
      %client.cmSendClientMessage(2475, "<color:FF0000>You closed the trade – step away and return to trade again.");
   }

   function SPTradeGui::reload()
   {
      SPTradeGui.scheduleInit();
   }
};
activatePackage(SPTradeGui);

SPTradeGui.setup();
SPTradeGui::reload();

function TradeGuiReload() { SPTradeGui::reload(); }
