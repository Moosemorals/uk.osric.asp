<?xml version="1.0" encoding="utf-8"?>
<!--
SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
SPDX-License-Identifier: MIT
-->
<xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">

  <!-- No script and no document(), loaded with XsltSettings.Default; the page is a tree rewrite. -->
  <xsl:output method="xml" omit-xml-declaration="yes" indent="no" />

  <xsl:template match="/reference">
    <nav class="type-index" aria-label="Types in this assembly">
      <xsl:apply-templates select="namespace" mode="index" />
    </nav>
    <xsl:apply-templates select="namespace" mode="body" />
  </xsl:template>

  <xsl:template name="namespace-label">
    <xsl:choose>
      <xsl:when test="@name != ''"><xsl:value-of select="@name" /></xsl:when>
      <xsl:otherwise><xsl:value-of select="/reference/@assembly" /></xsl:otherwise>
    </xsl:choose>
  </xsl:template>

  <xsl:template match="namespace" mode="index">
    <p class="namespace-name"><xsl:call-template name="namespace-label" /></p>
    <ul>
      <xsl:for-each select="type">
        <li><a href="#{@anchor}"><xsl:value-of select="@display" /></a></li>
      </xsl:for-each>
    </ul>
  </xsl:template>

  <xsl:template match="namespace" mode="body">
    <div class="namespace">
      <p class="namespace-name"><xsl:call-template name="namespace-label" /></p>
      <xsl:apply-templates select="type" />
    </div>
  </xsl:template>

  <xsl:template match="type">
    <section class="type">
      <h2 id="{@anchor}" title="{@id}"><code><xsl:value-of select="@display" /></code></h2>
      <xsl:apply-templates select="summary" mode="block" />
      <xsl:call-template name="inherited" />
      <xsl:apply-templates select="remarks" mode="block" />
      <xsl:if test="typeparam">
        <p class="label">Type parameters</p>
        <dl class="type-parameters">
          <xsl:for-each select="typeparam">
            <dt><code><xsl:value-of select="@name" /></code></dt>
            <dd><xsl:apply-templates select="node()" /></dd>
          </xsl:for-each>
        </dl>
      </xsl:if>
      <xsl:apply-templates select="member" />
    </section>
  </xsl:template>

  <xsl:template match="member">
    <div class="member">
      <h3 id="{@anchor}" title="{@id}"><span class="kind"><xsl:value-of select="@kind" /></span> <code><xsl:value-of select="@display" /></code></h3>
      <xsl:apply-templates select="summary" mode="block" />
      <xsl:call-template name="inherited" />
      <xsl:apply-templates select="remarks" mode="block" />
      <xsl:if test="typeparam">
        <h4>Type parameters</h4>
        <dl class="type-parameters">
          <xsl:for-each select="typeparam">
            <dt><code><xsl:value-of select="@name" /></code></dt>
            <dd><xsl:apply-templates select="node()" /></dd>
          </xsl:for-each>
        </dl>
      </xsl:if>
      <xsl:if test="param">
        <h4>Parameters</h4>
        <dl class="parameters">
          <xsl:for-each select="param">
            <dt><code><xsl:value-of select="@name" /></code></dt>
            <dd><xsl:apply-templates select="node()" /></dd>
          </xsl:for-each>
        </dl>
      </xsl:if>
      <xsl:if test="returns">
        <h4>Returns</h4>
        <div class="doc-returns"><xsl:apply-templates select="returns/node()" /></div>
      </xsl:if>
      <xsl:if test="value">
        <h4>Value</h4>
        <div class="doc-value"><xsl:apply-templates select="value/node()" /></div>
      </xsl:if>
      <xsl:if test="exception">
        <h4>Exceptions</h4>
        <dl class="exceptions">
          <xsl:for-each select="exception">
            <dt>
              <xsl:choose>
                <xsl:when test="@href"><a href="{@href}"><code><xsl:value-of select="@text" /></code></a></xsl:when>
                <xsl:otherwise><code><xsl:value-of select="@text" /></code></xsl:otherwise>
              </xsl:choose>
            </dt>
            <dd><xsl:apply-templates select="node()" /></dd>
          </xsl:for-each>
        </dl>
      </xsl:if>
    </div>
  </xsl:template>

  <xsl:template name="inherited">
    <xsl:choose>
      <xsl:when test="@inherited-href">
        <p class="inherited">Inherited from <a href="{@inherited-href}"><code><xsl:value-of select="@inherited-display" /></code></a>.</p>
      </xsl:when>
      <xsl:when test="@inherited-from">
        <p class="inherited">Inherited from <code><xsl:value-of select="@inherited-display" /></code>.</p>
      </xsl:when>
      <xsl:when test="@inherits = 'true'">
        <p class="inherited">Inherits its documentation.</p>
      </xsl:when>
    </xsl:choose>
  </xsl:template>

  <xsl:template match="summary" mode="block">
    <div class="doc-summary"><xsl:apply-templates select="node()" /></div>
  </xsl:template>

  <xsl:template match="remarks" mode="block">
    <div class="doc-remarks"><xsl:apply-templates select="node()" /></div>
  </xsl:template>

  <xsl:template match="see">
    <xsl:choose>
      <xsl:when test="@href"><a href="{@href}"><xsl:call-template name="see-text" /></a></xsl:when>
      <xsl:otherwise><code><xsl:call-template name="see-text" /></code></xsl:otherwise>
    </xsl:choose>
  </xsl:template>

  <xsl:template name="see-text">
    <xsl:choose>
      <xsl:when test="node()"><xsl:apply-templates select="node()" /></xsl:when>
      <xsl:when test="@text"><xsl:value-of select="@text" /></xsl:when>
      <xsl:when test="@langword"><xsl:value-of select="@langword" /></xsl:when>
      <xsl:otherwise><xsl:value-of select="@href" /></xsl:otherwise>
    </xsl:choose>
  </xsl:template>

  <xsl:template match="paramref|typeparamref"><code><xsl:value-of select="@name" /></code></xsl:template>
  <xsl:template match="c"><code><xsl:apply-templates select="node()" /></code></xsl:template>
  <xsl:template match="code"><pre class="source" tabindex="0"><code><xsl:value-of select="." /></code></pre></xsl:template>
  <xsl:template match="para"><p><xsl:apply-templates select="node()" /></p></xsl:template>
  <xsl:template match="b|strong"><strong><xsl:apply-templates select="node()" /></strong></xsl:template>
  <xsl:template match="i|em"><em><xsl:apply-templates select="node()" /></em></xsl:template>
  <xsl:template match="br"><br /></xsl:template>

  <xsl:template match="list">
    <xsl:choose>
      <xsl:when test="@type = 'table'">
        <table class="doc-table">
          <thead>
            <tr>
              <xsl:for-each select="listheader">
                <th scope="col"><xsl:apply-templates select="node()" /></th>
              </xsl:for-each>
            </tr>
          </thead>
          <tbody>
            <xsl:for-each select="item">
              <tr>
                <xsl:for-each select="term|description">
                  <td><xsl:apply-templates select="node()" /></td>
                </xsl:for-each>
              </tr>
            </xsl:for-each>
          </tbody>
        </table>
      </xsl:when>
      <xsl:when test="@type = 'number'">
        <ol><xsl:apply-templates select="item" mode="list-item" /></ol>
      </xsl:when>
      <xsl:otherwise>
        <ul><xsl:apply-templates select="item" mode="list-item" /></ul>
      </xsl:otherwise>
    </xsl:choose>
  </xsl:template>

  <xsl:template match="item" mode="list-item">
    <li>
      <xsl:if test="term">
        <strong><xsl:apply-templates select="term/node()" /></strong>
        <xsl:if test="description"> — </xsl:if>
      </xsl:if>
      <xsl:apply-templates select="description/node()" />
      <xsl:apply-templates select="node()[not(self::term) and not(self::description)]" />
    </li>
  </xsl:template>

  <xsl:template match="inheritdoc" />
</xsl:stylesheet>
