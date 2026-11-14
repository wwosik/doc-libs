# Build effects

What the generator writes for each marker, as PowerPoint writes its presets. Compared by the tests; regenerated with `DOCLIBS_UPDATE_EXPECTED=1`.

## The timing of `{.build}` on one paragraph

```xml
<p:timing>
  <p:tnLst>
    <p:par>
      <p:cTn id="1" dur="indefinite" restart="never" nodeType="tmRoot">
        <p:childTnLst>
          <p:seq concurrent="1" nextAc="seek">
            <p:cTn id="2" dur="indefinite" nodeType="mainSeq">
              <p:childTnLst>
                <p:par>
                  <p:cTn id="3" fill="hold">
                    <p:stCondLst>
                      <p:cond delay="indefinite" />
                    </p:stCondLst>
                    <p:childTnLst>
                      <p:par>
                        <p:cTn id="4" fill="hold">
                          <p:stCondLst>
                            <p:cond delay="0" />
                          </p:stCondLst>
                          <p:childTnLst>
                            <p:par>
                              <p:cTn id="5" presetID="1" presetClass="entr" presetSubtype="0" fill="hold" grpId="0" nodeType="clickEffect">
                                <p:stCondLst>
                                  <p:cond delay="0" />
                                </p:stCondLst>
                                <p:childTnLst>
                                  <p:set>
                                    <p:cBhvr>
                                      <p:cTn id="6" dur="1" fill="hold">
                                        <p:stCondLst>
                                          <p:cond delay="0" />
                                        </p:stCondLst>
                                      </p:cTn>
                                      <p:tgtEl>
                                        <p:spTgt spid="3">
                                          <p:txEl>
                                            <p:pRg st="0" end="0" />
                                          </p:txEl>
                                        </p:spTgt>
                                      </p:tgtEl>
                                      <p:attrNameLst>
                                        <p:attrName>style.visibility</p:attrName>
                                      </p:attrNameLst>
                                    </p:cBhvr>
                                    <p:to>
                                      <p:strVal val="visible" />
                                    </p:to>
                                  </p:set>
                                </p:childTnLst>
                              </p:cTn>
                            </p:par>
                          </p:childTnLst>
                        </p:cTn>
                      </p:par>
                    </p:childTnLst>
                  </p:cTn>
                </p:par>
              </p:childTnLst>
            </p:cTn>
            <p:prevCondLst>
              <p:cond evt="onPrev" delay="0">
                <p:tgtEl>
                  <p:sldTgt />
                </p:tgtEl>
              </p:cond>
            </p:prevCondLst>
            <p:nextCondLst>
              <p:cond evt="onNext" delay="0">
                <p:tgtEl>
                  <p:sldTgt />
                </p:tgtEl>
              </p:cond>
            </p:nextCondLst>
          </p:seq>
        </p:childTnLst>
      </p:cTn>
    </p:par>
  </p:tnLst>
  <p:bldLst>
    <p:bldP spid="3" grpId="0" build="p" />
  </p:bldLst>
</p:timing>
```

## The effect of `{.build=appear}`

```xml
<p:cTn id="5" presetID="1" presetClass="entr" presetSubtype="0" fill="hold" grpId="0" nodeType="clickEffect">
  <p:stCondLst>
    <p:cond delay="0" />
  </p:stCondLst>
  <p:childTnLst>
    <p:set>
      <p:cBhvr>
        <p:cTn id="6" dur="1" fill="hold">
          <p:stCondLst>
            <p:cond delay="0" />
          </p:stCondLst>
        </p:cTn>
        <p:tgtEl>
          <p:spTgt spid="3">
            <p:txEl>
              <p:pRg st="0" end="0" />
            </p:txEl>
          </p:spTgt>
        </p:tgtEl>
        <p:attrNameLst>
          <p:attrName>style.visibility</p:attrName>
        </p:attrNameLst>
      </p:cBhvr>
      <p:to>
        <p:strVal val="visible" />
      </p:to>
    </p:set>
  </p:childTnLst>
</p:cTn>
```

## The effect of `{.build=fly}`

```xml
<p:cTn id="5" presetID="2" presetClass="entr" presetSubtype="4" fill="hold" grpId="0" nodeType="clickEffect">
  <p:stCondLst>
    <p:cond delay="0" />
  </p:stCondLst>
  <p:childTnLst>
    <p:set>
      <p:cBhvr>
        <p:cTn id="6" dur="1" fill="hold">
          <p:stCondLst>
            <p:cond delay="0" />
          </p:stCondLst>
        </p:cTn>
        <p:tgtEl>
          <p:spTgt spid="3">
            <p:txEl>
              <p:pRg st="0" end="0" />
            </p:txEl>
          </p:spTgt>
        </p:tgtEl>
        <p:attrNameLst>
          <p:attrName>style.visibility</p:attrName>
        </p:attrNameLst>
      </p:cBhvr>
      <p:to>
        <p:strVal val="visible" />
      </p:to>
    </p:set>
    <p:anim calcmode="lin" valueType="num">
      <p:cBhvr additive="base">
        <p:cTn id="7" dur="500" fill="hold" />
        <p:tgtEl>
          <p:spTgt spid="3">
            <p:txEl>
              <p:pRg st="0" end="0" />
            </p:txEl>
          </p:spTgt>
        </p:tgtEl>
        <p:attrNameLst>
          <p:attrName>ppt_x</p:attrName>
        </p:attrNameLst>
      </p:cBhvr>
      <p:tavLst>
        <p:tav tm="0">
          <p:val>
            <p:strVal val="#ppt_x" />
          </p:val>
        </p:tav>
        <p:tav tm="100000">
          <p:val>
            <p:strVal val="#ppt_x" />
          </p:val>
        </p:tav>
      </p:tavLst>
    </p:anim>
    <p:anim calcmode="lin" valueType="num">
      <p:cBhvr additive="base">
        <p:cTn id="8" dur="500" fill="hold" />
        <p:tgtEl>
          <p:spTgt spid="3">
            <p:txEl>
              <p:pRg st="0" end="0" />
            </p:txEl>
          </p:spTgt>
        </p:tgtEl>
        <p:attrNameLst>
          <p:attrName>ppt_y</p:attrName>
        </p:attrNameLst>
      </p:cBhvr>
      <p:tavLst>
        <p:tav tm="0">
          <p:val>
            <p:strVal val="1+#ppt_h/2" />
          </p:val>
        </p:tav>
        <p:tav tm="100000">
          <p:val>
            <p:strVal val="#ppt_y" />
          </p:val>
        </p:tav>
      </p:tavLst>
    </p:anim>
  </p:childTnLst>
</p:cTn>
```

## The effect of `{.build=wipe}`

```xml
<p:cTn id="5" presetID="22" presetClass="entr" presetSubtype="8" fill="hold" grpId="0" nodeType="clickEffect">
  <p:stCondLst>
    <p:cond delay="0" />
  </p:stCondLst>
  <p:childTnLst>
    <p:set>
      <p:cBhvr>
        <p:cTn id="6" dur="1" fill="hold">
          <p:stCondLst>
            <p:cond delay="0" />
          </p:stCondLst>
        </p:cTn>
        <p:tgtEl>
          <p:spTgt spid="3">
            <p:txEl>
              <p:pRg st="0" end="0" />
            </p:txEl>
          </p:spTgt>
        </p:tgtEl>
        <p:attrNameLst>
          <p:attrName>style.visibility</p:attrName>
        </p:attrNameLst>
      </p:cBhvr>
      <p:to>
        <p:strVal val="visible" />
      </p:to>
    </p:set>
    <p:animEffect transition="in" filter="wipe(left)">
      <p:cBhvr>
        <p:cTn id="7" dur="500" />
        <p:tgtEl>
          <p:spTgt spid="3">
            <p:txEl>
              <p:pRg st="0" end="0" />
            </p:txEl>
          </p:spTgt>
        </p:tgtEl>
      </p:cBhvr>
    </p:animEffect>
  </p:childTnLst>
</p:cTn>
```

## The effect of `{.frag}`

```xml
<p:cTn id="5" presetID="1" presetClass="entr" presetSubtype="0" fill="hold" grpId="0" nodeType="clickEffect">
  <p:stCondLst>
    <p:cond delay="0" />
  </p:stCondLst>
  <p:childTnLst>
    <p:set>
      <p:cBhvr>
        <p:cTn id="6" dur="1" fill="hold">
          <p:stCondLst>
            <p:cond delay="0" />
          </p:stCondLst>
        </p:cTn>
        <p:tgtEl>
          <p:spTgt spid="3">
            <p:txEl>
              <p:pRg st="0" end="0" />
            </p:txEl>
          </p:spTgt>
        </p:tgtEl>
        <p:attrNameLst>
          <p:attrName>style.visibility</p:attrName>
        </p:attrNameLst>
      </p:cBhvr>
      <p:to>
        <p:strVal val="visible" />
      </p:to>
    </p:set>
  </p:childTnLst>
</p:cTn>
```

## The effect of `{.exit}`

```xml
<p:cTn id="5" presetID="1" presetClass="exit" presetSubtype="0" fill="hold" grpId="0" nodeType="clickEffect">
  <p:stCondLst>
    <p:cond delay="0" />
  </p:stCondLst>
  <p:childTnLst>
    <p:set>
      <p:cBhvr>
        <p:cTn id="6" dur="1" fill="hold">
          <p:stCondLst>
            <p:cond delay="0" />
          </p:stCondLst>
        </p:cTn>
        <p:tgtEl>
          <p:spTgt spid="3">
            <p:txEl>
              <p:pRg st="0" end="0" />
            </p:txEl>
          </p:spTgt>
        </p:tgtEl>
        <p:attrNameLst>
          <p:attrName>style.visibility</p:attrName>
        </p:attrNameLst>
      </p:cBhvr>
      <p:to>
        <p:strVal val="hidden" />
      </p:to>
    </p:set>
  </p:childTnLst>
</p:cTn>
```

## The effect of `{.emphasize}`

```xml
<p:cTn id="5" presetID="26" presetClass="emph" presetSubtype="0" fill="hold" grpId="0" nodeType="clickEffect">
  <p:stCondLst>
    <p:cond delay="0" />
  </p:stCondLst>
  <p:childTnLst>
    <p:animEffect transition="out" filter="fade">
      <p:cBhvr>
        <p:cTn id="6" dur="500" tmFilter="0, 0; .2, .5; .8, .5; 1, 0" />
        <p:tgtEl>
          <p:spTgt spid="3">
            <p:txEl>
              <p:pRg st="0" end="0" />
            </p:txEl>
          </p:spTgt>
        </p:tgtEl>
      </p:cBhvr>
    </p:animEffect>
    <p:animScale>
      <p:cBhvr>
        <p:cTn id="7" dur="250" autoRev="1" fill="hold" />
        <p:tgtEl>
          <p:spTgt spid="3">
            <p:txEl>
              <p:pRg st="0" end="0" />
            </p:txEl>
          </p:spTgt>
        </p:tgtEl>
      </p:cBhvr>
      <p:by x="105000" y="105000" />
    </p:animScale>
  </p:childTnLst>
</p:cTn>
```

## The build list of a panel, a table and a picture

```xml
<p:bldLst>
  <p:bldP spid="4" grpId="0" animBg="1" />
  <p:bldP spid="5" grpId="0" animBg="1" />
  <p:bldGraphic spid="6" grpId="0">
    <p:bldAsOne />
  </p:bldGraphic>
</p:bldLst>
```
