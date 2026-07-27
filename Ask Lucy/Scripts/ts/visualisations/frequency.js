"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
var d3 = require("d3");
var VizFrequency = /** @class */ (function () {
    function VizFrequency(_selector) {
        var svg = d3.select(_selector);
        svg.attr('background-color', 'white');
        svg.selectAll('*').remove();
        this.svg = svg;
    }
    VizFrequency.prototype.visualize = function (data) {
        // content area of your visualization
        var vis = this.svg.append('g');
        var margin = {
            top: 0,
            right: 0,
            bottom: 0,
            left: 0
        };
        var width = +this.svg.attr('width') - margin.left - margin.right;
        var height = +this.svg.attr('height') - margin.top - margin.bottom;
        var numberOfBars = Math.floor(width / 5);
        var xScale = d3.scaleLinear()
            .range([0, vis.attr("width")])
            .domain([0, numberOfBars]);
        var yScale = d3.scaleLinear()
            .range([0, vis.attr("height")])
            .domain([-128, 0]);
        // Remove any rects already in the selector
        vis.selectAll("rect").remove();
        // Add a transparent rect so scaleY scales the appropriate height
        vis.append("rect")
            .attr("class", "background")
            .attr({
            x: 0,
            y: 0,
            width: width,
            height: height
        });
        var aggregatedData = this.aggregate(data, numberOfBars);
        // Set the transform to force the scaleY
        vis.attr("style", "transform-origin: " + (width / 2) + "px " + (height / 2) + "px; transform: scaleY(-1);");
        var rect = vis.selectAll("rect.frequency-bar").data(aggregatedData);
        rect.enter()
            .append("rect")
            .attr("x", function (d, i) {
            return xScale(i);
        })
            .attr("width", function () {
            return vis.attr("width") / numberOfBars;
        })
            .attr("y", 0)
            .attr("class", "frequency-bar");
        rect.attr("height", function (d) {
            var rectHeight = yScale(d);
            return rectHeight > 1 ? rectHeight : 1;
        });
    };
    // Bucket the data and average them
    VizFrequency.prototype.aggregate = function (data, numberOfBars) {
        var aggregated = new Float32Array(numberOfBars);
        for (var i = 0; i < numberOfBars; i++) {
            var lowerBound = Math.floor(i / numberOfBars * data.length);
            var upperBound = Math.floor((i + 1) / numberOfBars * data.length);
            var bucket = data.slice(lowerBound, upperBound);
            aggregated[i] = bucket.reduce(function (acc, d) {
                return acc + d;
            }, 0) / bucket.length;
        }
        return aggregated;
    };
    return VizFrequency;
}());
exports.default = VizFrequency;
//# sourceMappingURL=frequency.js.map